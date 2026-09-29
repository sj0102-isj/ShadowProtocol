using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 쿼터뷰(아이소메트릭) 카메라. 고정 각도로 플레이어를 부드럽게 따라간다.
    /// 각도를 고정해야 경비 시야 콘이 항상 같은 방식으로 읽힌다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;   // 따라갈 대상. 비어 있으면 Start 에서 플레이어를 찾는다

        [Header("Rig")]
        [Tooltip("내려다보는 각도. 55도 정도가 시야 콘 가독성과 벽 가림의 균형점.")]
        public float pitch = 55f;     // 위아래 각도 (X축 회전)
        public float yaw = 45f;       // 좌우 각도 (Y축 회전). 45도라 대각선에서 본다
        public float distance = 22f;  // 대상에서 얼마나 떨어져 있는가

        [Header("Follow")]
        public float followLerp = 8f;   // 따라가는 빠르기. 클수록 즉각적
        [Tooltip("플레이어가 바라보는 쪽을 살짝 미리 보여준다.")]
        public float lookAhead = 1.5f;

        [Header("Wall Fade")]
        [Tooltip(
            "플레이어 둘레 이 반경 안의 바닥을 카메라에서 가리는 벽·선반을 반투명하게 한다.\n" +
            "벽 높이 2.6m 에 55도로 내려다보면 벽 뒤 약 1.8m 의 바닥이 가려진다. 경비는 그 자리의 " +
            "플레이어를 제대로 보고 있는데(벽은 카메라와 플레이어 사이에 있다) 발밑 시야 콘만 벽에 가려서 " +
            "'콘이 안 보이는데 들킨다'가 됐다. 판정은 그대로 두고, 가리는 벽만 비춰 보이게 한다.")]
        public float fadeRadius = 1.8f;
        [Range(0f, 1f)] public float fadeAlpha = 0.3f;

        Camera cam;
        Vector3 currentFocus;   // 카메라가 지금 보고 있는 지점. 목표로 서서히 다가간다

        // 반투명으로 바꾼 벽 하나의 기록. 원래 벽은 그림자만 남기고, 같은 모양의 반투명 사본을 대신 보여준다.
        // (벽 머티리얼 자체를 반투명으로 바꾸면 그림자가 사라지고, 빌드에서 셰이더 변형이 빠질 수 있다)
        class Faded
        {
            public Renderer wall;
            public GameObject ghost;
            public Renderer[] decor;      // 벽에 얹은 장식(테두리 등). 반투명일 때는 잠깐 끈다
            public UnityEngine.Rendering.ShadowCastingMode shadow;
            public float lastHit;
            public bool on;
        }

        readonly Dictionary<Renderer, Faded> faded = new Dictionary<Renderer, Faded>();
        readonly HashSet<Renderer> hitThisFrame = new HashSet<Renderer>();
        readonly Dictionary<Material, Material> ghostMaterials = new Dictionary<Material, Material>();
        static readonly RaycastHit[] fadeHits = new RaycastHit[16];

        void Awake()
        {
            cam = GetComponent<Camera>();

            // orthographic(직교) 투영 = 원근이 없다. 멀리 있는 것도 같은 크기로 보인다.
            // 쿼터뷰 게임에서 이걸 쓰는 이유는, 원근이 있으면 화면 가장자리의 시야 콘이
            // 찌그러져 보여서 "이 안에 들어가면 들킨다"를 눈으로 가늠하기 어려워지기 때문이다.
            cam.orthographic = true;
            cam.orthographicSize = 10f;   // 화면 세로 절반이 월드에서 몇 m인가

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Start()
        {
            if (target == null)
            {
                var player = Object.FindFirstObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }

            // 첫 프레임에는 보간 없이 곧바로 자리를 잡는다.
            // 안 그러면 게임 시작 순간 카메라가 원점에서 플레이어까지 쭉 날아간다.
            if (target != null)
            {
                currentFocus = target.position;
                ApplyPosition();
            }
        }

        /// <summary>
        /// LateUpdate 에서 카메라를 옮기는 것이 원칙이다.
        /// Update 에서 옮기면 플레이어가 아직 안 움직인 상태의 위치를 따라가서 한 프레임 떨린다.
        /// </summary>
        void LateUpdate()
        {
            if (target == null) return;

            // 바라보는 쪽을 조금 앞서 보여주면 진행 방향의 정보를 더 볼 수 있다.
            Vector3 desired = target.position + target.forward * lookAhead;

            // 1 - Exp(-k·dt) 는 프레임 속도와 무관하게 같은 감속 곡선을 만드는 보간 계수다.
            // 그냥 Lerp(a, b, k * dt) 를 쓰면 프레임이 높은 컴퓨터에서 더 빨리 따라붙는다.
            currentFocus = Vector3.Lerp(currentFocus, desired, 1f - Mathf.Exp(-followLerp * Time.deltaTime));

            ApplyPosition();
            UpdateWallFade();
        }

        /// <summary>
        /// 플레이어 둘레 바닥 25곳과 머리에서 카메라 쪽으로 레이를 쏴서, 걸리는 벽·선반·상인방을 반투명하게 한다.
        /// 시야 콘은 건드리지 않는다 — 콘은 원래대로 벽에서 잘리고, 가려져 있던 부분이 보이게 될 뿐이다.
        /// </summary>
        void UpdateWallFade()
        {
            hitThisFrame.Clear();
            Vector3 toCamera = -transform.forward;
            Vector3 center = target.position;
            float step = fadeRadius * 0.5f;

            for (int ix = -2; ix <= 2; ix++)
                for (int iz = -2; iz <= 2; iz++)
                    CollectOccluders(center + new Vector3(ix * step, 0.1f, iz * step), toCamera);
            CollectOccluders(center + Vector3.up * 1.7f, toCamera);

            // 게임이 멈춘 화면(메뉴·붙잡힘 연출)에서도 벽 상태가 맞아야 해서 unscaled 시간을 쓴다.
            float now = Time.unscaledTime;
            foreach (Renderer r in hitThisFrame)
            {
                if (!faded.TryGetValue(r, out Faded f)) faded[r] = f = MakeFaded(r);
                f.lastHit = now;
                SetFaded(f, true);
            }

            // 바로 되돌리면 경계에서 벽이 깜박인다. 0.25초 동안 한 번도 안 걸렸을 때 되돌린다.
            foreach (Faded f in faded.Values)
                if (f.on && now - f.lastHit > 0.25f) SetFaded(f, false);
        }

        void CollectOccluders(Vector3 from, Vector3 dir)
        {
            int n = Physics.RaycastNonAlloc(from, dir, fadeHits, 60f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = fadeHits[i].collider;
                if (col == null) continue;
                string name = col.name;   // StageBuilder 가 붙인 이름. 벽·선반·크롤 상인방만 비춘다
                if (!name.StartsWith("Wall_") && !name.StartsWith("Shelf_") && !name.StartsWith("Lintel_")) continue;
                var r = col.GetComponent<Renderer>();
                if (r != null) hitThisFrame.Add(r);
            }
        }

        Faded MakeFaded(Renderer wall)
        {
            var f = new Faded { wall = wall, shadow = wall.shadowCastingMode };

            var ghost = new GameObject("FadeGhost");
            ghost.transform.SetParent(wall.transform, false);
            // 벽은 정적(static)이라 실행 중에는 메시가 씬 전체의 합친 메시로 바뀌어 있다(정적 배칭).
            // 그걸 복사하면 엉뚱한 모양이 되므로, 벽이 원래 정육면체였다는 점을 이용해 기본 큐브를 쓴다.
            ghost.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
            var gr = ghost.AddComponent<MeshRenderer>();
            gr.sharedMaterial = GhostMaterial(wall.sharedMaterial);
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gr.receiveShadows = false;
            ghost.SetActive(false);
            f.ghost = ghost;

            var decor = new List<Renderer>();
            foreach (Renderer child in wall.GetComponentsInChildren<Renderer>(true))
                if (child != wall && child != gr) decor.Add(child);
            f.decor = decor.ToArray();
            return f;
        }

        void SetFaded(Faded f, bool on)
        {
            if (f.on == on || f.wall == null) return;
            f.on = on;
            // 원래 벽은 그림자만 드리운다 — 바닥의 그림자가 갑자기 사라지면 더 눈에 띈다.
            f.wall.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : f.shadow;
            f.ghost.SetActive(on);
            foreach (Renderer d in f.decor) if (d != null) d.enabled = !on;
        }

        /// <summary>
        /// 벽 색 그대로, 반투명하게. 시야 콘과 같은 셰이더(Stealth/FOVCone)를 쓰는 이유는
        /// 이미 모든 씬이 쓰고 있어서 빌드에 반드시 들어가기 때문이다.
        /// </summary>
        Material GhostMaterial(Material source)
        {
            if (source != null && ghostMaterials.TryGetValue(source, out Material m)) return m;
            Shader shader = Shader.Find("Stealth/FOVCone");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(shader) { name = "WallFadeGhost" };
            Color c = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.gray;
            c.a = fadeAlpha;
            m.SetColor("_BaseColor", c);
            if (source != null) ghostMaterials[source] = m;
            return m;
        }

        static Mesh cubeMesh;
        static Mesh CubeMesh
        {
            get
            {
                if (cubeMesh == null) cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                if (cubeMesh == null)
                {
                    // 임시 큐브는 콜라이더가 붙어 있어서, 한 프레임이라도 남으면 시야 레이에 걸린다. 즉시 지운다.
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                    DestroyImmediate(tmp);
                }
                return cubeMesh;
            }
        }

        void OnDestroy()
        {
            foreach (Material m in ghostMaterials.Values) if (m != null) Destroy(m);
        }

        /// <summary>보간 없이 곧바로 대상 위로 옮긴다. 순간이동(붙잡혀서 시작 지점으로)에 쓴다.</summary>
        public void Snap()
        {
            if (target == null) return;
            currentFocus = target.position + target.forward * lookAhead;
            ApplyPosition();
        }

        /// <summary>바라보는 지점에서 고정 각도·거리만큼 뒤로 물러난 자리에 카메라를 놓는다.</summary>
        void ApplyPosition()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            // rot * Vector3.forward = 그 각도에서의 '앞' 방향.
            // 초점에서 그 반대로 distance 만큼 물러나면 초점을 바라보는 자리가 된다.
            transform.SetPositionAndRotation(currentFocus - rot * Vector3.forward * distance, rot);
        }

        /// <summary>입력을 화면 기준으로 변환할 때 쓰는 기준 축.</summary>
        //
        // 카메라가 45도 기울어 있어서, W를 눌렀을 때 월드의 +Z 가 아니라 "화면에서 위쪽"으로
        // 가야 한다. 그 '화면에서 위쪽'에 해당하는 월드 방향이 이 값이다.
        // 카메라는 아래를 내려다보고 있으므로 y 성분을 버리고 평평하게 만든다.
        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
            }
        }

        /// <summary>화면 기준 오른쪽에 해당하는 월드 방향.</summary>
        public Vector3 FlatRight
        {
            get
            {
                Vector3 r = transform.right;
                r.y = 0f;
                return r.sqrMagnitude < 0.0001f ? Vector3.right : r.normalized;
            }
        }
    }
}
