using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 공중 순찰 드론. 벽을 무시하고 직선으로 날아다니며 바로 아래를 원형으로 비춘다.
    /// 경비와 달리 엄폐물 뒤에 숨는 것이 통하지 않고, 머리 위를 막는 것(크롤 통로 상인방,
    /// 은신처)이나 정전만이 통한다.
    ///
    /// NavMesh 를 쓰지 않으므로 경로는 그냥 웨이포인트 사이 직선 보간이다.
    /// </summary>
    public class SurveillanceDrone : MonoBehaviour
    {
        public static readonly List<SurveillanceDrone> All = new List<SurveillanceDrone>();

        [Header("Route")]
        public Transform[] waypoints;
        public float speed = 3.2f;
        [Tooltip("웨이포인트에 도착했을 때 멈춰서 훑는 시간(초).")]
        public float hoverPause = 1.0f;

        [Header("Scan")]
        [Tooltip("바닥에 비추는 원의 반경(m).")]
        public float scanRadius = 4f;
        [Tooltip(
            "원 안에 있을 때 경계 게이지가 차는 속도. 1이면 1초 만에 발각.\n" +
            "2.5 면 0.4초. 원을 스치는 정도가 아니면 무조건 잡히므로 " +
            "'파란 원에는 들어가지 않는다'가 규칙이 된다. " +
            "낮추면 드론을 무시하고 뛰어서 지나가는 것이 정답이 되어 버린다.")]
        public float detectRate = 2.5f;
        [Tooltip("원을 벗어난 뒤 게이지가 빠지는 속도. 스치고 지나간 것이 누적되지 않도록 빠르게 둔다.")]
        public float decayRate = 1.5f;

        [Header("Visual")]
        public Renderer discRenderer;
        public Color idleColor = new Color(0.45f, 0.75f, 1f, 0.20f);
        public Color alertColor = new Color(1f, 0.30f, 0.30f, 0.34f);

        public float Awareness { get; private set; }

        /// <summary>HUD 가 경비와 드론 중 더 높은 경계도를 표시할 수 있도록.</summary>
        public static float MaxAwareness
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i] != null && All[i].Awareness > max) max = All[i].Awareness;
                }
                return max;
            }
        }

        PlayerController player;
        MaterialPropertyBlock mpb;
        int target;
        float pauseTimer;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake() => mpb = new MaterialPropertyBlock();

        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
            if (waypoints != null && waypoints.Length > 0 && waypoints[0] != null)
                transform.position = Flight(waypoints[0].position);
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;
            if (gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive))
            {
                UpdateVisual();
                return;
            }

            Fly();
            UpdateAwareness();
            UpdateVisual();
        }

        Vector3 Flight(Vector3 waypoint) =>
            new Vector3(waypoint.x, transform.position.y, waypoint.z);

        void Fly()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
                return;
            }

            Vector3 goal = Flight(waypoints[target].position);
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            if ((transform.position - goal).sqrMagnitude > 0.01f) return;

            pauseTimer = hoverPause;
            target = (target + 1) % waypoints.Length;
        }

        void UpdateAwareness()
        {
            if (Spots())
            {
                Awareness = Mathf.Min(1f, Awareness + detectRate * Time.deltaTime);
                if (Awareness >= 1f) Detect();
            }
            else
            {
                Awareness = Mathf.Max(0f, Awareness - decayRate * Time.deltaTime);
            }
        }

        bool Spots()
        {
            if (player == null) return false;
            if (BlackoutSystem.PlayerIsInvisible) return false;
            if (player.IsHidden || player.IsUnderCover) return false;

            Vector3 a = transform.position;
            Vector3 b = player.EyePoint;
            var flat = new Vector2(b.x - a.x, b.z - a.z);
            if (flat.sqrMagnitude > scanRadius * scanRadius) return false;

            // 머리 위를 막는 것이 있으면 못 본다. 크롤 통로의 상인방이 여기서 엄폐가 된다.
            if (Physics.Linecast(a, b, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerController>() == null) return false;
            }

            return true;
        }

        /// <summary>
        /// 발각되어 시작 지점으로 돌아갈 때 경계 게이지를 지운다.
        /// 이걸 안 하면 복귀 직후에도 게이지가 남아 있어, 드론 원에 발만 들여도 즉시 다시 잡힌다.
        /// 경비의 ResetToPatrol 과 같은 역할이다.
        /// </summary>
        public void ResetScan() => Awareness = 0f;

        void Detect()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null) return;

            gm.ReportDetection();
            gm.Capture();
            Awareness = 0f;
        }

        void UpdateVisual()
        {
            if (discRenderer == null) return;

            Color color = Color.Lerp(idleColor, alertColor, Awareness);
            if (BlackoutSystem.Instance != null)
                color.a *= 1f - BlackoutSystem.Instance.Darkness * 0.75f;

            discRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            discRenderer.SetPropertyBlock(mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.75f, 1f, 0.6f);
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, 0.05f, transform.position.z), scanRadius);
        }
    }
}
