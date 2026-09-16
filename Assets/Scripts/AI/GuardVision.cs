using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 경비의 시야 판정 + 바닥에 깔리는 시야 콘 메시 생성.
    /// 콘은 벽에 가려 잘리기 때문에 "경비가 실제로 보는 영역"과 화면 표시가 일치한다.
    /// </summary>
    //
    // 이 스크립트는 두 가지 일을 한다. 둘을 구분해서 읽으면 훨씬 쉽다.
    //   1) 판정  : CanSee()   — 플레이어가 실제로 보이는가? (게임 규칙)
    //   2) 표현  : DrawCone() — 바닥에 부채꼴을 그린다 (화면에 보이는 것)
    //
    // 둘 다 같은 규칙(거리·각도·벽)을 쓰기 때문에 화면의 초록 부채꼴 안에 들어가면 정말로 들키고,
    // 밖에 있으면 안 들킨다. 플레이어가 화면만 보고 판단할 수 있게 하는 것이 이 설계의 목적이다.
    public class GuardVision : MonoBehaviour
    {
        [Header("Vision")]
        public float viewDistance = 12f;    // 볼 수 있는 최대 거리(m)

        // [Range] 를 붙이면 인스펙터에 숫자 칸 대신 슬라이더가 나오고, 범위 밖 값을 못 넣게 막아준다.
        [Range(10f, 180f)] public float fovAngle = 90f;  // 시야각. 90이면 정면 기준 좌우 45도씩

        public float eyeHeight = 1.65f;      // 눈높이(m). 여기서 플레이어를 향해 선을 긋는다
        [Tooltip(
            "등 뒤라도 이 거리 안에 들어오면 인기척으로 알아챈다.\n" +
            "격자 한 칸이 2m 이므로 2.2 는 '한 칸 조금 넘게' 였다. 등 뒤로 지나가려면 " +
            "두 칸을 띄워야 해서 답답했다. 1.3 은 '바로 옆 칸까지만' 이라 " +
            "한 칸 띄우고 지나가는 플레이가 가능해진다.")]
        public float proximityRadius = 1.3f; // 시야각과 무관하게 감지되는 반경

        [Header("Cone Mesh")]
        [Tooltip("높을수록 부드럽지만 레이캐스트가 늘어난다.")]
        public int rayCount = 64;           // 부채꼴을 몇 조각으로 나눌지. 조각마다 레이를 한 번 쏜다
        public float coneHeight = 0.06f;    // 바닥에서 살짝 띄우는 높이. 0이면 바닥과 겹쳐 지글거린다
        [Tooltip("콘 클리핑에 쓰는 레이 높이. 크롤 통로 상인방 바닥(1.25)보다 높아야 콘이 통로 아래로 새어나가지 않고, 은신 소품(0.90)보다는 높아야 엄폐 그림이 유지된다.")]
        public float rayHeight = 1.5f;

        [Header("Colors")]
        // Color 는 (빨강, 초록, 파랑, 투명도)를 0~1 로 담는다. 네 번째가 알파(투명도)다.
        public Color patrolColor = new Color(0.30f, 0.95f, 0.55f, 0.16f);      // 초록 — 평상시
        public Color suspiciousColor = new Color(1.00f, 0.90f, 0.25f, 0.22f);  // 노랑 — 의심
        public Color searchColor = new Color(1.00f, 0.60f, 0.15f, 0.26f);      // 주황 — 수색
        public Color chaseColor = new Color(1.00f, 0.25f, 0.25f, 0.32f);       // 빨강 — 추격

        /// <summary>true면 아무것도 못 본다. 콘도 함께 숨겨서 화면과 판정이 어긋나지 않게 한다.</summary>
        // { get; set; } — 밖에서 읽기도 쓰기도 가능. 두꺼비집(정전)이 이 값을 켠다.
        public bool Blinded { get; set; }

        /// <summary>경비의 눈 위치. 발밑 좌표에서 눈높이만큼 올린 지점.</summary>
        public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;

        // --- 부채꼴 메시를 만들기 위한 부품들 ---
        // 메시(Mesh) = 3D 모양 데이터. "꼭짓점 목록" + "그 점들을 어떤 순서로 삼각형으로 이을지" 두 가지다.
        MeshFilter coneFilter;      // "어떤 모양인가"를 담는 부품
        MeshRenderer coneRenderer;  // "그 모양을 어떻게 그릴 것인가"를 담는 부품
        Mesh coneMesh;              // 실제 모양 데이터. 매 프레임 새로 계산한다
        Material coneMaterial;      // 색과 셰이더
        Vector3[] vertices;         // 꼭짓점 좌표 목록
        int[] triangles;            // 꼭짓점 세 개씩 묶어 삼각형을 만드는 순서표
        Color currentColor;         // 지금 색. 목표 색으로 서서히 변한다

        // 레이캐스트 결과를 담을 그릇. static 이라 모든 경비가 하나를 돌려 쓴다.
        // 한 번에 한 명씩만 쓰기 때문에 문제가 없고, 매 프레임 배열을 새로 만들지 않아 성능에 좋다.
        static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        void Awake()
        {
            BuildConeObject();
            currentColor = patrolColor;
        }

        /// <summary>
        /// LateUpdate — 모든 Update 가 끝난 뒤에 호출된다.
        /// 경비가 이번 프레임에 이동·회전을 마친 다음 콘을 그려야 콘이 한 프레임 늦게 따라오지 않는다.
        /// </summary>
        void LateUpdate()
        {
            if (coneRenderer != null) coneRenderer.enabled = !Blinded;
            if (!Blinded) DrawCone();
        }

        /// <summary>
        /// 콘을 그릴 자식 오브젝트를 코드로 만들어 붙인다.
        /// 프리팹을 쓰지 않는 이유는 경비 자체가 StageBuilder 로 생성되기 때문이다.
        /// </summary>
        void BuildConeObject()
        {
            var go = new GameObject("ViewCone");     // 빈 오브젝트를 씬에 새로 만든다

            // SetParent(transform, false) — 경비의 자식으로 넣는다.
            // 두 번째 인자 false = "월드 위치를 유지하지 말고 부모 기준 좌표를 그대로 써라".
            // true 면 유니티가 위치를 보정해서 엉뚱한 자리로 간다.
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, coneHeight, 0f);
            go.transform.localRotation = Quaternion.identity;   // identity = 회전 없음

            coneFilter = go.AddComponent<MeshFilter>();
            coneRenderer = go.AddComponent<MeshRenderer>();
            coneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coneRenderer.receiveShadows = false;   // 시야 표시일 뿐이라 그림자를 주고받을 이유가 없다

            // Shader.Find 는 이름으로 셰이더를 찾는다. 없으면 null 이 오므로 대비책을 둔다.
            Shader shader = Shader.Find("Stealth/FOVCone");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            coneMaterial = new Material(shader);
            coneRenderer.sharedMaterial = coneMaterial;

            // { name = ... } 는 객체 초기화자. new Mesh() 한 다음 name 을 넣는 것과 같다.
            coneMesh = new Mesh { name = "ViewConeMesh" };
            coneMesh.MarkDynamic();   // "매 프레임 바뀐다"고 알려주면 유니티가 더 빠르게 처리한다
            coneFilter.mesh = coneMesh;

            rayCount = Mathf.Max(8, rayCount);   // 최소 8 보장. 0이 들어오면 배열이 깨진다

            // 꼭짓점 개수 = 중심점 1개 + 부채꼴 테두리 (rayCount + 1)개
            vertices = new Vector3[rayCount + 2];

            // 삼각형 하나당 꼭짓점 번호 3개가 필요하므로 rayCount * 3
            triangles = new int[rayCount * 3];

            // 순서표는 한 번만 만들면 된다. 매 프레임 바뀌는 건 꼭짓점 "위치"뿐이고
            // "어떻게 잇는지"는 그대로이기 때문이다.
            //   0번(중심) - (i+1)번 - (i+2)번  →  피자 조각 하나
            for (int i = 0; i < rayCount; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        /// <summary>
        /// OnDestroy — 오브젝트가 사라질 때 호출된다.
        /// 코드로 new 해서 만든 Mesh 와 Material 은 유니티가 자동으로 치워주지 않는다.
        /// 여기서 직접 Destroy 하지 않으면 씬을 다시 로드할 때마다 메모리에 쌓인다(메모리 누수).
        /// </summary>
        void OnDestroy()
        {
            if (coneMesh != null) Destroy(coneMesh);
            if (coneMaterial != null) Destroy(coneMaterial);
        }

        /// <summary>상태에 맞는 색으로 콘을 물들인다. GuardAI 가 매 프레임 불러준다.</summary>
        public void SetStateColor(GuardState state, float awareness)
        {
            // switch 식 — if/else 사슬을 짧게 쓴 것.
            // _ 는 "그 외 전부". 여기서는 Patrol 과 Return 이 여기로 온다.
            Color target = state switch
            {
                GuardState.Chase => chaseColor,
                GuardState.Search => searchColor,
                GuardState.Suspicious => suspiciousColor,
                // 순찰 중에는 경계 게이지(awareness, 0~1)에 따라 초록↔노랑 사이를 오간다.
                _ => Color.Lerp(patrolColor, suspiciousColor, awareness)
            };

            // 정전 중에는 콘을 흐리게 해서 화면이 어두워진 느낌을 해치지 않게 한다.
            if (BlackoutSystem.Instance != null) target.a *= 1f - BlackoutSystem.Instance.Darkness * 0.75f;

            // Time.unscaledDeltaTime 을 쓴 이유: Time.deltaTime 은 일시정지(timeScale = 0)일 때 0이 된다.
            // 색 변화는 게임 시간이 멈춰도 자연스럽게 이어지는 편이 낫다.
            currentColor = Color.Lerp(currentColor, target, 10f * Time.unscaledDeltaTime);

            // 셰이더 안의 "_BaseColor" 라는 변수에 색을 밀어 넣는다.
            if (coneMaterial != null) coneMaterial.SetColor("_BaseColor", currentColor);
        }

        /// <summary>
        /// 부채꼴 메시를 매 프레임 새로 만든다.
        /// 정면 기준 왼쪽 끝부터 오른쪽 끝까지 레이를 rayCount 개 쏘고,
        /// 각 레이가 벽에 막힌 거리까지만 꼭짓점을 찍는다. 그래서 콘이 벽에서 잘린다.
        /// </summary>
        void DrawCone()
        {
            if (coneMesh == null) return;

            float step = fovAngle / rayCount;   // 레이 사이의 각도 간격
            float start = -fovAngle * 0.5f;     // 왼쪽 끝 각도. 90도 시야면 -45도부터 시작
            Vector3 origin = transform.position + Vector3.up * rayHeight;

            vertices[0] = Vector3.zero;   // 중심점. 부모(경비) 기준 좌표라 0이 곧 경비 발밑이다

            for (int i = 0; i <= rayCount; i++)
            {
                float angle = start + step * i;

                // Quaternion * Vector3 = 그 벡터를 회전시킨 결과.
                // 정면(Vector3.forward)을 y축 기준으로 angle 만큼 돌린 방향을 얻는다.
                Vector3 localDir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                // TransformDirection = 경비 기준 방향 → 월드 기준 방향으로 변환.
                // 레이캐스트는 월드 좌표를 쓰기 때문에 변환이 필요하다.
                Vector3 worldDir = transform.TransformDirection(localDir);

                // 꼭짓점은 부모 기준 좌표로 넣어야 하므로 localDir 을 쓴다. 거리는 벽에 막힌 지점까지.
                vertices[i + 1] = localDir * CastVisionRay(origin, worldDir, viewDistance);
            }

            coneMesh.Clear();                // 이전 내용을 비우고
            coneMesh.vertices = vertices;    // 새 꼭짓점을 넣고
            coneMesh.triangles = triangles;  // 잇는 순서를 알려주면 모양이 완성된다
            coneMesh.RecalculateBounds();    // 카메라에 보이는지 판단할 범위를 다시 잡는다
        }

        /// <summary>
        /// 벽까지의 거리를 잰다. 사람(플레이어/경비)은 벽이 아니므로 건너뛰고,
        /// 그 뒤에 있는 진짜 벽까지 계속 본다. 단순 Raycast를 쓰면 사람 뒤의 벽을 놓쳐
        /// 콘이 벽을 뚫고 나가는 문제가 생긴다.
        /// </summary>
        float CastVisionRay(Vector3 origin, Vector3 direction, float maxDistance)
        {
            // Physics.Raycast 는 "제일 먼저 맞은 것 하나"만 준다. 그게 사람이면 벽을 놓친다.
            // RaycastNonAlloc 은 맞은 것을 전부 배열에 담아주므로, 사람을 걸러내고 벽만 볼 수 있다.
            //   ~0 = 모든 레이어 / 트리거(통과 가능한 영역)는 무시
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, maxDistance,
                ~0, QueryTriggerInteraction.Ignore);

            float nearest = maxDistance;   // 아무것도 없으면 최대 거리까지 뻗는다

            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i].collider;
                if (col == null) continue;

                // GetComponentInParent = 자신과 부모들을 거슬러 올라가며 그 부품을 찾는다.
                // 맞은 것이 몸통 캡슐이어도 부모인 플레이어 오브젝트를 찾아낼 수 있다.
                if (col.GetComponentInParent<PlayerController>() != null) continue;   // 사람은 벽이 아니다
                if (col.GetComponentInParent<GuardAI>() != null) continue;

                // NonAlloc 은 가까운 순서로 정렬해 주지 않는다. 그래서 직접 최솟값을 찾는다.
                if (hitBuffer[i].distance < nearest) nearest = hitBuffer[i].distance;
            }

            return nearest;
        }

        /// <summary>
        /// 플레이어가 보이는가. closeness 는 0(시야 끝)~1(코앞) 로, 경계 게이지 속도에 쓰인다.
        /// </summary>
        //
        // 검사 순서가 중요하다. 싼 검사(값 비교)를 먼저, 비싼 검사(레이캐스트)를 마지막에 한다.
        // 대부분은 앞쪽에서 false 로 빠져나가므로 레이캐스트까지 가는 일이 드물다.
        public bool CanSee(PlayerController player, out float closeness)
        {
            closeness = 0f;   // out 매개변수는 함수를 나가기 전에 반드시 값이 있어야 한다

            if (player == null) return false;
            if (Blinded) return false;

            // 정전 중에는 거리·각도와 무관하게 아무도 볼 수 없다.
            if (BlackoutSystem.PlayerIsInvisible) return false;

            // 은신처 안에 들어가 있거나, 크롤 통로 상인방 아래에 기어 들어가 있을 때도 마찬가지다.
            if (player.IsHidden || player.IsUnderCover) return false;

            Vector3 targetPoint = player.EyePoint;
            Vector3 toTarget = targetPoint - EyePosition;   // 눈에서 플레이어로 향하는 벡터
            float distance = toTarget.magnitude;            // 그 벡터의 길이 = 실제 거리

            // 아주 가까우면 등 뒤라도 인기척으로 알아챈다.
            bool withinProximity = distance <= proximityRadius;

            // 1) 거리 검사
            if (distance > viewDistance && !withinProximity) return false;

            // 2) 각도 검사 — 코앞이 아닐 때만 한다
            if (!withinProximity)
            {
                Vector3 flat = toTarget;
                flat.y = 0f;   // 위아래 차이는 무시. 2층이 없는 게임이라 수평 각도만 보면 된다

                // Vector3.Angle 은 두 벡터 사이의 각도를 도(°)로 준다.
                // 시야각 90도면 정면에서 45도 이내여야 보인다.
                if (Vector3.Angle(transform.forward, flat) > fovAngle * 0.5f) return false;
            }

            // 3) 장애물 검사 — 눈에서 플레이어까지 직선을 긋고 중간에 뭔가 걸리는지 본다.
            // Linecast 는 두 점 사이만 검사한다(Raycast 는 방향 + 거리로 검사).
            if (Physics.Linecast(EyePosition, targetPoint, out RaycastHit hit, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                // 맞은 게 플레이어 본인이면 가로막힌 게 아니라 제대로 본 것이다.
                // 플레이어가 아닌 무언가에 막혔으면 안 보인다.
                if (hit.collider.GetComponentInParent<PlayerController>() == null) return false;
            }

            // 4) 콘과 같은 높이에서 한 번 더 — 화면과 판정을 강제로 일치시킨다.
            //
            // 위의 3번은 눈(1.65)에서 플레이어 눈(서면 1.2, 기어가면 0.45)으로 '비스듬히' 긋는다.
            // 그래서 크롤 통로 상인방(바닥 1.25) 아래로 선이 빠져나가는 각이 생긴다.
            // 반면 콘은 rayHeight(1.5)에서 수평으로 쏘므로 그 자리에서 잘린다.
            //   → 화면에는 콘이 안 보이는데 발각되는, 플레이어 입장에서 가장 억울한 상황.
            //
            // 콘이 쓰는 것과 똑같은 높이·똑같은 수평선으로 한 번 더 검사하면 둘이 어긋날 수 없다.
            // 낮은 엄폐물(0.90)은 1.5보다 낮아 이 검사에 걸리지 않으므로 엄폐는 그대로 작동한다.
            Vector3 flatEye = transform.position + Vector3.up * rayHeight;
            var flatTarget = new Vector3(targetPoint.x, flatEye.y, targetPoint.z);

            if (Physics.Linecast(flatEye, flatTarget, out RaycastHit flatHit, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (flatHit.collider.GetComponentInParent<PlayerController>() == null) return false;
            }

            // 여기까지 왔으면 보인다. 얼마나 가까운지를 0~1 로 환산해 돌려준다.
            // Clamp01 = 0보다 작으면 0, 1보다 크면 1로 잘라준다.
            float reference = Mathf.Max(viewDistance, proximityRadius);
            closeness = Mathf.Clamp01(1f - distance / reference);
            return true;
        }

        /// <summary>
        /// OnDrawGizmosSelected — 씬 뷰에서 이 오브젝트를 선택했을 때만 그려지는 보조선.
        /// 게임 화면에는 안 나오고 에디터에서 배치를 확인할 때만 쓴다. 빌드에는 포함되지 않는다.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 origin = transform.position + Vector3.up * rayHeight;
            Vector3 left = Quaternion.Euler(0f, -fovAngle * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, fovAngle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawLine(origin, origin + left * viewDistance);   // 시야 왼쪽 경계선
            Gizmos.DrawLine(origin, origin + right * viewDistance);  // 시야 오른쪽 경계선

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, proximityRadius);  // 인기척 반경
        }
    }
}
