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
    //
    // 경비(GuardAI)와 비교하면 구조가 훨씬 단순하다.
    //   - 상태머신이 없다. 항상 "날면서 아래를 본다" 하나뿐
    //   - 길찾기가 없다. 벽을 무시하고 직선으로 난다
    //   - 시야각이 없다. 아래쪽 원 안이면 무조건 본다
    // 그래서 플레이어에게 요구하는 대응이 경비와 완전히 달라진다.
    public class SurveillanceDrone : MonoBehaviour
    {
        // 씬에 있는 모든 드론의 명단. GuardAI.All 과 같은 방식이다.
        public static readonly List<SurveillanceDrone> All = new List<SurveillanceDrone>();

        [Header("Route")]
        public Transform[] waypoints;     // 순찰 지점들
        public float speed = 3.2f;        // 비행 속도(m/s)
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
        public Renderer discRenderer;     // 바닥에 깔리는 원판
        public Color idleColor = new Color(0.45f, 0.75f, 1f, 0.20f);   // 파랑 — 평상시
        public Color alertColor = new Color(1f, 0.30f, 0.30f, 0.34f);  // 빨강 — 게이지가 찼을 때

        /// <summary>이 드론의 경계 게이지 0~1.</summary>
        public float Awareness { get; private set; }

        /// <summary>HUD 가 경비와 드론 중 더 높은 경계도를 표시할 수 있도록.</summary>
        // static 프로퍼티 — 특정 드론이 아니라 "전체 중 최댓값"이므로 클래스에 속한다.
        // get { ... } 형태는 값을 돌려주기 전에 계산이 필요할 때 쓴다.
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
        MaterialPropertyBlock mpb;   // 머티리얼 사본을 만들지 않고 색만 바꾸는 도구
        int target;                  // 지금 향하는 웨이포인트 번호
        float pauseTimer;            // 도착 후 멈춰 있는 시간
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 본문이 한 줄이면 => 로 줄여 쓸 수 있다. 아래 두 줄은 { All.Add(this); } 와 같은 뜻이다.
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake() => mpb = new MaterialPropertyBlock();

        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();

            // 첫 웨이포인트 위로 순간이동시켜 시작한다. 높이는 지금 높이를 유지한다.
            if (waypoints != null && waypoints.Length > 0 && waypoints[0] != null)
                transform.position = Flight(waypoints[0].position);
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;

            // 메뉴이거나 붙잡힘 연출 중이면 날지도 않고 감지하지도 않는다.
            // 다만 원판 색은 계속 갱신해야 암전 중에도 자연스럽게 보인다.
            if (gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive))
            {
                UpdateVisual();
                return;
            }

            Fly();
            UpdateAwareness();
            UpdateVisual();
        }

        /// <summary>
        /// 웨이포인트의 수평 좌표만 쓰고 높이는 드론의 현재 높이를 유지한다.
        /// 웨이포인트는 바닥에 찍혀 있으므로 그대로 쓰면 드론이 땅으로 내려앉는다.
        /// </summary>
        Vector3 Flight(Vector3 waypoint) =>
            new Vector3(waypoint.x, transform.position.y, waypoint.z);

        /// <summary>웨이포인트를 순서대로 직선 비행한다. 길찾기가 없어 벽을 그냥 통과한다.</summary>
        void Fly()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            // 도착해서 멈춰 있는 중이면 타이머만 줄이고 끝낸다.
            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
                return;
            }

            Vector3 goal = Flight(waypoints[target].position);

            // MoveTowards = 목표를 향해 정해진 거리만큼 다가간다. 목표를 지나치지 않는다.
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            // 아직 멀면 여기서 끝. 0.01f 는 "거리 0.1m 이내"라는 뜻 (0.1의 제곱).
            if ((transform.position - goal).sqrMagnitude > 0.01f) return;

            // 도착 — 잠시 머물고 다음 지점으로
            pauseTimer = hoverPause;
            target = (target + 1) % waypoints.Length;   // % 로 마지막 다음이 처음이 된다
        }

        /// <summary>경계 게이지를 채우거나 뺀다. 경비와 달리 "의심" 단계가 없고 바로 발각이다.</summary>
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

        /// <summary>지금 플레이어가 스캔 원 안에 있고 머리 위가 뚫려 있는가.</summary>
        bool Spots()
        {
            if (player == null) return false;
            if (BlackoutSystem.PlayerIsInvisible) return false;          // 정전 중에는 못 본다
            if (player.IsHidden || player.IsUnderCover) return false;    // 은신처·통로 아래도 안전

            Vector3 a = transform.position;   // 드론 (공중)
            Vector3 b = player.EyePoint;      // 플레이어

            // 수평 거리만 잰다. 드론은 높이 4.5m 에 있으므로 3D 거리로 재면 항상 멀어 보인다.
            // Vector2 에 x 와 z 를 담으면 위에서 내려다본 평면 거리가 된다.
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

        /// <summary>게이지가 꽉 찼을 때. 게임 매니저에게 붙잡힘을 알린다.</summary>
        void Detect()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null) return;

            gm.ReportDetection();
            gm.Capture();
            Awareness = 0f;
        }

        /// <summary>바닥 원판의 색을 경계도에 따라 파랑↔빨강으로 바꾼다.</summary>
        void UpdateVisual()
        {
            if (discRenderer == null) return;

            // Lerp 의 세 번째 인자로 Awareness(0~1)를 그대로 넣으면
            // 게이지가 차오르는 만큼 색이 점점 빨개진다.
            Color color = Color.Lerp(idleColor, alertColor, Awareness);

            // 정전 중에는 원판도 흐려진다.
            if (BlackoutSystem.Instance != null)
                color.a *= 1f - BlackoutSystem.Instance.Darkness * 0.75f;

            discRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            discRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>씬 뷰에서 선택했을 때 스캔 범위를 바닥에 원으로 그려준다.</summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.75f, 1f, 0.6f);
            // y 를 0.05 로 고정해 바닥에 붙여 그린다. 드론 높이에 그리면 범위를 가늠하기 어렵다.
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, 0.05f, transform.position.z), scanRadius);
        }
    }
}
