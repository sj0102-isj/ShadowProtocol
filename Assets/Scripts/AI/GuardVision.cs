using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 경비의 시야 판정 + 바닥에 깔리는 시야 콘 메시 생성.
    /// 콘은 벽에 가려 잘리기 때문에 "경비가 실제로 보는 영역"과 화면 표시가 일치한다.
    /// </summary>
    public class GuardVision : MonoBehaviour
    {
        [Header("Vision")]
        public float viewDistance = 12f;
        [Range(10f, 180f)] public float fovAngle = 90f;
        public float eyeHeight = 1.65f;
        [Tooltip("등 뒤라도 이 거리 안에 들어오면 인기척으로 알아챈다.")]
        public float proximityRadius = 2.2f;

        [Header("Cone Mesh")]
        [Tooltip("높을수록 부드럽지만 레이캐스트가 늘어난다.")]
        public int rayCount = 64;
        public float coneHeight = 0.06f;
        [Tooltip("콘 클리핑에 쓰는 레이 높이. 크롤 통로 상인방 바닥(1.25)보다 높아야 콘이 통로 아래로 새어나가지 않고, 은신 소품(0.90)보다는 높아야 엄폐 그림이 유지된다.")]
        public float rayHeight = 1.5f;

        [Header("Colors")]
        public Color patrolColor = new Color(0.30f, 0.95f, 0.55f, 0.16f);
        public Color suspiciousColor = new Color(1.00f, 0.90f, 0.25f, 0.22f);
        public Color searchColor = new Color(1.00f, 0.60f, 0.15f, 0.26f);
        public Color chaseColor = new Color(1.00f, 0.25f, 0.25f, 0.32f);

        /// <summary>true면 아무것도 못 본다. 콘도 함께 숨겨서 화면과 판정이 어긋나지 않게 한다.</summary>
        public bool Blinded { get; set; }

        public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;

        MeshFilter coneFilter;
        MeshRenderer coneRenderer;
        Mesh coneMesh;
        Material coneMaterial;
        Vector3[] vertices;
        int[] triangles;
        Color currentColor;

        static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        void Awake()
        {
            BuildConeObject();
            currentColor = patrolColor;
        }

        void LateUpdate()
        {
            if (coneRenderer != null) coneRenderer.enabled = !Blinded;
            if (!Blinded) DrawCone();
        }

        void BuildConeObject()
        {
            var go = new GameObject("ViewCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, coneHeight, 0f);
            go.transform.localRotation = Quaternion.identity;

            coneFilter = go.AddComponent<MeshFilter>();
            coneRenderer = go.AddComponent<MeshRenderer>();
            coneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coneRenderer.receiveShadows = false;

            Shader shader = Shader.Find("Stealth/FOVCone");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            coneMaterial = new Material(shader);
            coneRenderer.sharedMaterial = coneMaterial;

            coneMesh = new Mesh { name = "ViewConeMesh" };
            coneMesh.MarkDynamic();
            coneFilter.mesh = coneMesh;

            rayCount = Mathf.Max(8, rayCount);
            vertices = new Vector3[rayCount + 2];
            triangles = new int[rayCount * 3];
            for (int i = 0; i < rayCount; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        void OnDestroy()
        {
            if (coneMesh != null) Destroy(coneMesh);
            if (coneMaterial != null) Destroy(coneMaterial);
        }

        public void SetStateColor(GuardState state, float awareness)
        {
            Color target = state switch
            {
                GuardState.Chase => chaseColor,
                GuardState.Search => searchColor,
                GuardState.Suspicious => suspiciousColor,
                _ => Color.Lerp(patrolColor, suspiciousColor, awareness)
            };

            // 정전 중에는 콘을 흐리게 해서 화면이 어두워진 느낌을 해치지 않게 한다.
            if (BlackoutSystem.Instance != null) target.a *= 1f - BlackoutSystem.Instance.Darkness * 0.75f;

            currentColor = Color.Lerp(currentColor, target, 10f * Time.unscaledDeltaTime);
            if (coneMaterial != null) coneMaterial.SetColor("_BaseColor", currentColor);
        }

        void DrawCone()
        {
            if (coneMesh == null) return;

            float step = fovAngle / rayCount;
            float start = -fovAngle * 0.5f;
            Vector3 origin = transform.position + Vector3.up * rayHeight;

            vertices[0] = Vector3.zero;
            for (int i = 0; i <= rayCount; i++)
            {
                float angle = start + step * i;
                Vector3 localDir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 worldDir = transform.TransformDirection(localDir);

                vertices[i + 1] = localDir * CastVisionRay(origin, worldDir, viewDistance);
            }

            coneMesh.Clear();
            coneMesh.vertices = vertices;
            coneMesh.triangles = triangles;
            coneMesh.RecalculateBounds();
        }

        /// <summary>
        /// 벽까지의 거리를 잰다. 사람(플레이어/경비)은 벽이 아니므로 건너뛰고,
        /// 그 뒤에 있는 진짜 벽까지 계속 본다. 단순 Raycast를 쓰면 사람 뒤의 벽을 놓쳐
        /// 콘이 벽을 뚫고 나가는 문제가 생긴다.
        /// </summary>
        float CastVisionRay(Vector3 origin, Vector3 direction, float maxDistance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, maxDistance,
                ~0, QueryTriggerInteraction.Ignore);

            float nearest = maxDistance;
            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<PlayerController>() != null) continue;
                if (col.GetComponentInParent<GuardAI>() != null) continue;
                if (hitBuffer[i].distance < nearest) nearest = hitBuffer[i].distance;
            }

            return nearest;
        }

        /// <summary>
        /// 플레이어가 보이는가. closeness 는 0(시야 끝)~1(코앞) 로, 경계 게이지 속도에 쓰인다.
        /// </summary>
        public bool CanSee(PlayerController player, out float closeness)
        {
            closeness = 0f;
            if (player == null) return false;
            if (Blinded) return false;

            // 정전 중에는 거리·각도와 무관하게 아무도 볼 수 없다.
            if (BlackoutSystem.PlayerIsInvisible) return false;

            // 은신처 안에 들어가 있거나, 크롤 통로 상인방 아래에 기어 들어가 있을 때도 마찬가지다.
            if (player.IsHidden || player.IsUnderCover) return false;

            Vector3 targetPoint = player.EyePoint;
            Vector3 toTarget = targetPoint - EyePosition;
            float distance = toTarget.magnitude;

            bool withinProximity = distance <= proximityRadius;

            if (distance > viewDistance && !withinProximity) return false;

            if (!withinProximity)
            {
                Vector3 flat = toTarget;
                flat.y = 0f;
                if (Vector3.Angle(transform.forward, flat) > fovAngle * 0.5f) return false;
            }

            if (Physics.Linecast(EyePosition, targetPoint, out RaycastHit hit, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerController>() == null) return false;
            }

            float reference = Mathf.Max(viewDistance, proximityRadius);
            closeness = Mathf.Clamp01(1f - distance / reference);
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 origin = transform.position + Vector3.up * rayHeight;
            Vector3 left = Quaternion.Euler(0f, -fovAngle * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, fovAngle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawLine(origin, origin + left * viewDistance);
            Gizmos.DrawLine(origin, origin + right * viewDistance);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
        }
    }
}
