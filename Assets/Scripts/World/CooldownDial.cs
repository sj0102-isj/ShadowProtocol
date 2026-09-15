using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 바닥에 깔리는 원형 쿨다운 표시. 남은 시간에 비례해 부채꼴이 줄어든다.
    ///
    /// 화면 UI가 아니라 월드 메시로 만든 이유는, 쿼터뷰에서는 물건이 놓인 자리에
    /// 직접 표시가 있어야 "저기 가면 있다"가 읽히기 때문이다.
    /// 메시를 매 프레임 다시 만드는 방식은 경비 시야 콘(GuardVision)과 같다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CooldownDial : MonoBehaviour
    {
        [Tooltip("쿨다운을 읽어올 대상. 비어 있으면 부모에서 찾는다.")]
        public Objective source;

        public float radius = 1.4f;
        [Tooltip("높을수록 원이 매끄럽지만 정점이 늘어난다.")]
        public int segments = 48;
        public Color color = new Color(0.45f, 1f, 0.95f, 0.40f);

        MeshFilter filter;
        MeshRenderer view;
        Mesh mesh;
        Material material;
        Vector3[] vertices;   // 꼭짓점 좌표 (중심 1개 + 테두리 segments+1개)
        int[] triangles;      // 삼각형을 만드는 순서표

        void Awake()
        {
            // GetComponentInParent = 자신과 부모를 거슬러 올라가며 찾는다.
            // 이 다이얼은 자료(Objective)의 자식으로 붙기 때문에 부모에서 찾으면 된다.
            if (source == null) source = GetComponentInParent<Objective>();

            filter = GetComponent<MeshFilter>();
            view = GetComponent<MeshRenderer>();
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.receiveShadows = false;

            Shader shader = Shader.Find("Stealth/FOVCone");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            view.sharedMaterial = material;

            segments = Mathf.Max(8, segments);
            vertices = new Vector3[segments + 2];
            triangles = new int[segments * 3];

            // 삼각형 순서표는 한 번만 만든다. 매 프레임 바뀌는 건 꼭짓점 위치뿐이다.
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;          // 중심
                triangles[i * 3 + 1] = i + 1;  // 테두리 점
                triangles[i * 3 + 2] = i + 2;  // 그 다음 테두리 점
            }

            mesh = new Mesh { name = "CooldownDial" };
            mesh.MarkDynamic();
            filter.mesh = mesh;

            view.enabled = false;   // 쿨다운이 없을 때는 보이지 않는다
        }

        /// <summary>코드로 만든 Mesh·Material 은 직접 치워야 메모리에 쌓이지 않는다.</summary>
        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        void LateUpdate()
        {
            float fill = 0f;

            // 남은 시간 ÷ 전체 시간 = 남은 비율. 60초 중 42초 남았으면 0.7 이 된다.
            if (source != null && source.respawnDelay > 0f)
                fill = Mathf.Clamp01(source.RespawnRemaining / source.respawnDelay);

            // 비율이 0이면 아예 렌더러를 꺼버린다. 빈 메시를 그리는 것보다 싸다.
            view.enabled = fill > 0f;
            if (fill <= 0f) return;

            Draw(fill);
        }

        /// <summary>
        /// 12시 방향에서 시계 방향으로 fill 만큼 채운 부채꼴.
        /// fill 이 0으로 가면 정점이 한 점에 모여 자연스럽게 사라진다.
        /// </summary>
        void Draw(float fill)
        {
            float sweep = 360f * fill;     // 그릴 각도. 100%면 360도(완전한 원)
            float step = sweep / segments; // 조각 하나당 각도

            vertices[0] = Vector3.zero;    // 중심점

            for (int i = 0; i <= segments; i++)
            {
                float angle = step * i;

                // 각도 → 좌표 변환. sin 이 x, cos 이 z 인 이유는 12시(북쪽)에서 시작해
                // 시계 방향으로 돌게 하기 위해서다.
                //   angle 0 → (sin0, cos0) = (0, 1) = 북쪽
                //   angle 90 → (1, 0) = 동쪽
                // Deg2Rad 는 도(°)를 라디안으로 바꾸는 상수다. Mathf 의 삼각함수는 라디안을 받는다.
                vertices[i + 1] = new Vector3(
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius,
                    0f,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius);
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
