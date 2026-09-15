using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Stealth
{
    /// <summary>
    /// 경비 AI 상태머신. 시야/소음 입력을 받아 상태를 전이하고 NavMesh로 움직인다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(GuardVision))]
    public class GuardAI : MonoBehaviour
    {
        public static readonly List<GuardAI> All = new List<GuardAI>();

        /// <summary>
        /// 동전 응답자를 고를 때 경로 길이를 재는 용도. 순차적으로만 쓰므로 하나면 된다.
        /// 필드 초기화로 만들면 Unity가 "생성자에서 NavMeshPath를 만들 수 없다"며 막으므로
        /// 처음 쓸 때 만든다.
        /// </summary>
        static NavMeshPath pathBuffer;

        [Header("Patrol")]
        public Transform[] waypoints;
        public float patrolSpeed = 2.2f;
        [Tooltip("순찰 지점 도착 후, 고개를 돌리기 전에 가만히 서 있는 시간.")]
        public float arrivePause = 1.2f;
        [Tooltip("좌/우를 확인할 때 그 방향을 바라본 채 머무는 시간.")]
        public float scanHold = 0.9f;
        [Tooltip("좌우로 확인하는 각도.")]
        public float lookAroundAngle = 65f;
        [Tooltip("제자리에서 고개를 돌리는 속도(도/초). 느릴수록 지나갈 틈이 넓어진다.")]
        public float scanTurnSpeed = 85f;
        [Tooltip("수색 중 제자리에서 좌우로 살피는 각도. 기준 각도에서 ± 이만큼만 움직인다.")]
        public float searchLookAngle = 40f;
        [Tooltip("동전을 줍고 주위를 살피는 각도. 이 동안은 시야가 꺼져 있다.")]
        public float coinLookAngle = 30f;

        [Header("Search")]
        [Tooltip("의심한 뒤 확인하러 출발하기까지 멈춰 서 있는 시간.")]
        public float suspicionPause = 1.6f;
        public float searchSpeed = 3.4f;
        public float searchDuration = 8f;
        public float searchRadius = 5f;

        [Header("Detection (쉬운 난이도 기준)")]
        [Tooltip("시야 끝에서 경계가 차오르는 속도 (초당).")]
        public float baseDetectRate = 0.38f;
        [Tooltip("코앞에서 추가로 붙는 속도 (초당).")]
        public float closeDetectRate = 0.95f;
        [Tooltip("시야에서 벗어났을 때 경계가 빠지는 속도 (초당).")]
        public float decayRate = 0.5f;
        [Tooltip("시야를 벗어난 뒤 경계가 줄기 시작하기까지의 유예.")]
        public float decayDelay = 0.8f;
        [Range(0f, 1f)] public float suspiciousThreshold = 0.35f;

        [Header("Hearing")]
        public float hearingMultiplier = 1f;
        [Tooltip("던진 동전 앞에서 두리번거리는 시간(초).")]
        public float coinInspectSeconds = 2.5f;

        [Header("Indicator")]
        public Transform indicator;

        public GuardState State { get; private set; } = GuardState.Patrol;
        public float Awareness { get; private set; }
        public bool SeesPlayer { get; private set; }

        NavMeshAgent agent;
        GuardVision vision;
        PlayerController player;
        Renderer indicatorRenderer;
        MaterialPropertyBlock mpb;

        int waypointIndex;
        int scanStep;
        float scanTimer;
        float stateTimer;
        float waitTimer;
        bool wasFrozen;
        Coin targetCoin;
        float coinDwellTimer;
        float decayTimer;
        Vector3 pointOfInterest;
        Vector3 lastKnownPlayerPos;
        Quaternion lookTarget;

        /// <summary>제자리에서 두리번거릴 때의 기준 각도. 도착 시점에 한 번만 고정한다.</summary>
        float lookBaseYaw;
        bool lookBaseSet;
        bool hasDestination;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            vision = GetComponent<GuardVision>();
            agent.updateRotation = false;
            agent.speed = patrolSpeed;
            agent.stoppingDistance = 0.15f;
            lookTarget = transform.rotation;
            mpb = new MaterialPropertyBlock();
            if (indicator != null) indicatorRenderer = indicator.GetComponent<Renderer>();
        }

        void OnEnable()
        {
            All.Add(this);
            NoiseSystem.OnNoise += HandleNoise;
        }

        void OnDisable()
        {
            All.Remove(this);
            NoiseSystem.OnNoise -= HandleNoise;
        }

        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
            if (waypoints != null && waypoints.Length > 0 && waypoints[0] != null)
                GoTo(waypoints[0].position, patrolSpeed);
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;
            bool frozen = gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive);

            if (frozen)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                wasFrozen = true;

                // 암전이 진행되는 동안에도 붉은 콘이 보이도록 시각 갱신은 계속한다.
                vision.SetStateColor(State, Awareness);
                return;
            }

            if (wasFrozen)
            {
                wasFrozen = false;
                if (agent.isOnNavMesh) agent.isStopped = false;
            }

            // 동전을 줍는 동안은 고개를 숙이고 있는 것으로 본다.
            vision.Blinded = InspectingCoin;

            UpdateAwareness();
            TickState();
            UpdateRotation();
            UpdateIndicator();
            vision.SetStateColor(State, Awareness);
        }

        // ---------------------------------------------------------------- 인지

        void UpdateAwareness()
        {
            SeesPlayer = vision.CanSee(player, out float closeness);

            if (SeesPlayer)
            {
                decayTimer = decayDelay;
                lastKnownPlayerPos = player.transform.position;

                float alertBoost = 1f;
                if (StealthGameManager.Instance != null)
                    alertBoost += StealthGameManager.Instance.AlertLevel * 0.25f;

                float rate = (baseDetectRate + closeDetectRate * closeness)
                             * player.VisibilityMultiplier
                             * alertBoost;

                Awareness = Mathf.Min(1f, Awareness + rate * Time.deltaTime);

                if (Awareness >= 1f && State != GuardState.Chase)
                {
                    EnterChase();
                }
                else if (Awareness >= suspiciousThreshold && State == GuardState.Patrol)
                {
                    EnterSuspicious(player.transform.position);
                }
            }
            else
            {
                if (decayTimer > 0f) decayTimer -= Time.deltaTime;
                else Awareness = Mathf.Max(0f, Awareness - decayRate * Time.deltaTime);
            }
        }

        /// <summary>
        /// 발소리 같은 보통 소음. 들은 경비는 전부 대략적인 방향으로 움직인다.
        /// 동전은 이 경로를 타지 않는다. AlertNearestToCoin 이 한 명만 골라서 직접 보낸다.
        /// </summary>
        void HandleNoise(Vector3 position, float radius, float strength, Transform source)
        {
            if (State == GuardState.Chase) return;
            if (source != null && source.GetComponent<Coin>() != null) return;

            float distance = Vector3.Distance(transform.position, position);
            if (distance > radius * hearingMultiplier) return;

            // 발소리로는 방향만 어렴풋이 안다. 정확한 자리를 집어주면 너무 정확해진다.
            Vector3 guess = position + Random.insideUnitSphere * 1.5f;
            guess.y = position.y;

            if (State == GuardState.Patrol || State == GuardState.Return) EnterSearch(guess);
            else if (State == GuardState.Suspicious) pointOfInterest = guess;
        }

        /// <summary>
        /// 동전 소리를 들은 경비 중 제일 가까운 한 명만 주우러 보낸다.
        ///
        /// 예전에는 경비마다 "내가 제일 가까운가"를 각자 판단했다. 그런데 먼저 반응한 경비가
        /// 곧바로 수색 상태로 바뀌면서 "응답 가능" 후보에서 빠져버렸고, 그래서 그다음 경비도
        /// "나보다 가까운 애가 없다"고 판단해 따라왔다. 결국 범위 안 전원이 몰려왔다.
        /// 한곳에서 한 번만 정하면 이 순서 문제가 사라진다.
        /// </summary>
        public static void AlertNearestToCoin(Vector3 position, float radius, Coin coin)
        {
            // 동전이 벽에 끼거나 소품 위에 올라앉았어도 경비가 갈 수 있는 자리로 맞춘다.
            if (!NavMesh.SamplePosition(position, out NavMeshHit landing, 4f, NavMesh.AllAreas)) return;

            GuardAI nearest = null;
            float best = float.MaxValue;

            for (int i = 0; i < All.Count; i++)
            {
                GuardAI guard = All[i];
                if (guard == null || !guard.CanRespondToCoin) continue;

                float reach = radius * guard.hearingMultiplier;

                // 걸어가는 거리는 직선 거리보다 항상 길다. 먼저 싸게 거른다.
                if ((guard.transform.position - landing.position).sqrMagnitude > reach * reach) continue;

                float walk = WalkDistance(guard.transform.position, landing.position, reach);
                if (walk >= best) continue;

                nearest = guard;
                best = walk;
            }

            if (nearest == null) return;

            nearest.targetCoin = coin;
            nearest.EnterSearch(landing.position);
        }

        /// <summary>
        /// 실제로 걸어가야 하는 거리. 벽 하나를 사이에 둔 경비는 직선으로는 코앞이어도
        /// 돌아가는 길이 멀다. 직선 거리로 고르면 그 경비가 뽑혀서, 벽 너머에 있던 애가
        /// 한참을 돌아 달려오는 이상한 그림이 된다.
        ///
        /// 갈 수 없거나 한계를 넘으면 무한대를 돌려준다. 한계를 넘는 순간 바로 끊으므로
        /// 맵 반대편 경비 때문에 경로를 끝까지 더하지 않는다.
        /// </summary>
        static float WalkDistance(Vector3 from, Vector3 to, float limit)
        {
            pathBuffer ??= new NavMeshPath();

            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, pathBuffer)) return float.MaxValue;
            if (pathBuffer.status != NavMeshPathStatus.PathComplete) return float.MaxValue;

            Vector3[] corners = pathBuffer.corners;
            float total = 0f;

            for (int i = 1; i < corners.Length; i++)
            {
                total += Vector3.Distance(corners[i - 1], corners[i]);
                if (total > limit) return float.MaxValue;
            }

            return total;
        }

        /// <summary>동전 앞에 도착해 줍고 있는 중인가.</summary>
        bool InspectingCoin => targetCoin != null && coinDwellTimer > 0f;

        /// <summary>순찰·복귀 중인 경비만 동전을 주우러 갈 수 있다.</summary>
        bool CanRespondToCoin => State == GuardState.Patrol || State == GuardState.Return;

        // ------------------------------------------------------------ 상태머신

        void TickState()
        {
            stateTimer += Time.deltaTime;

            switch (State)
            {
                case GuardState.Patrol: TickPatrol(); break;
                case GuardState.Suspicious: TickSuspicious(); break;
                case GuardState.Search: TickSearch(); break;
                case GuardState.Chase: TickChase(); break;
                case GuardState.Return: TickReturn(); break;
            }
        }

        /// <summary>
        /// 순찰 지점에 도착하면 곧바로 다음 지점으로 가지 않고 한 박자 쉰다.
        ///   0단계 정지 -> 1단계 좌측 확인 -> 2단계 우측 확인 -> 3단계 정면 복귀 -> 다음 지점
        /// 회전이 실제로 끝난 뒤부터 대기 시간을 세기 때문에, 각 방향에서 확실히 멈춰 선다.
        /// </summary>
        void TickPatrol()
        {
            if (waypoints == null || waypoints.Length == 0) return;

            if (!ReachedDestination())
            {
                scanStep = 0;
                scanTimer = 0f;
                return;
            }

            // 기준 각도는 도착했을 때 한 번만 잡는다. 매 프레임 현재 각도를 다시 읽으면
            // 자기 출력이 입력으로 되먹임되어 회전이 누적된다.
            if (!lookBaseSet)
            {
                lookBaseYaw = waypoints[waypointIndex] != null
                    ? waypoints[waypointIndex].eulerAngles.y
                    : transform.eulerAngles.y;
                lookBaseSet = true;
            }

            float offset = scanStep switch
            {
                1 => -lookAroundAngle,
                2 => lookAroundAngle,
                _ => 0f
            };
            lookTarget = Quaternion.Euler(0f, lookBaseYaw + offset, 0f);

            // 아직 그 방향을 보고 있지 않다면 대기 시간을 세지 않는다.
            if (Quaternion.Angle(transform.rotation, lookTarget) > 6f) return;

            scanTimer += Time.deltaTime;

            float hold = scanStep switch
            {
                0 => arrivePause,
                3 => 0.4f,
                _ => scanHold
            };
            if (scanTimer < hold) return;

            scanTimer = 0f;
            scanStep++;

            if (scanStep <= 3) return;

            scanStep = 0;
            waypointIndex = (waypointIndex + 1) % waypoints.Length;
            if (waypoints[waypointIndex] != null)
                GoTo(waypoints[waypointIndex].position, patrolSpeed);
        }

        void TickSuspicious()
        {
            if (agent.isOnNavMesh) agent.isStopped = true;

            // 아직 보이는 동안에는 마지막 목격 지점을 계속 갱신한다.
            if (SeesPlayer) pointOfInterest = lastKnownPlayerPos;

            Vector3 dir = pointOfInterest - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) lookTarget = Quaternion.LookRotation(dir, Vector3.up);

            // 한 번 의심했으면 반드시 가서 확인한다.
            // 그냥 제자리로 돌아가 버리면 Search 상태에 도달할 일이 거의 없어져
            // 상태머신의 절반이 죽는다.
            if (stateTimer > suspicionPause) EnterSearch(pointOfInterest);
        }

        void TickSearch()
        {
            if (targetCoin != null)
            {
                // 동전이 못 가는 자리에 떨어지면 ReachedDestination 이 영영 참이 되지 않는다.
                // 여기서 끊지 않으면 그 경비는 수색 상태로 굳어 순찰을 영영 재개하지 않는다.
                if (stateTimer > searchDuration + coinInspectSeconds)
                {
                    targetCoin = null;
                    coinDwellTimer = 0f;
                    EnterReturn();
                    return;
                }

                TickCoinInvestigation();
                return;
            }

            if (ReachedDestination())
            {
                waitTimer -= Time.deltaTime;
                LookAround(2f, searchLookAngle);

                if (waitTimer <= 0f)
                {
                    waitTimer = 1.2f;
                    GoTo(RandomPointNear(pointOfInterest, searchRadius), searchSpeed);
                }
            }

            if (stateTimer > searchDuration) EnterReturn();
        }

        /// <summary>
        /// 던져진 동전을 확인하러 가는 동작. 주우면 잠깐 두리번거린 뒤 순찰로 돌아간다.
        ///
        /// </summary>
        void TickCoinInvestigation()
        {
            if (!ReachedDestination())
            {
                coinDwellTimer = 0f;
                return;
            }

            // 도착했으면 줍는다. 동전이 이미 사라졌거나 플레이어가 다시 던져서
            // 다른 자리로 날아갔다면 빈손으로 그 자리를 잠시 살핀다.
            if (targetCoin.gameObject.activeSelf &&
                (targetCoin.transform.position - transform.position).sqrMagnitude < 4f)
            {
                targetCoin.PickUp();
            }

            coinDwellTimer += Time.deltaTime;
            LookAround(2.4f, coinLookAngle);

            if (coinDwellTimer < coinInspectSeconds) return;

            targetCoin = null;
            coinDwellTimer = 0f;
            EnterReturn();
        }

        /// <summary>
        /// 발각은 곧 체포다. 쫓아가지 않고 그 자리에 서서 플레이어를 가리킨다.
        /// 암전이 끝나면 ResetToPatrol 로 순찰 상태로 돌아간다.
        /// </summary>
        void TickChase()
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
        }

        void TickReturn()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                SetState(GuardState.Patrol);
                return;
            }

            if (ReachedDestination()) SetState(GuardState.Patrol);
        }

        // --------------------------------------------------------------- 전이

        void EnterSuspicious(Vector3 point)
        {
            pointOfInterest = point;
            SetState(GuardState.Suspicious);
        }

        void EnterChase()
        {
            SetState(GuardState.Chase);
            Awareness = 1f;

            var gm = StealthGameManager.Instance;
            if (gm == null) return;

            gm.ReportDetection();
            gm.Capture();
        }

        void EnterSearch(Vector3 point)
        {
            pointOfInterest = point;
            waitTimer = 0.4f;
            SetState(GuardState.Search);
            GoTo(point, searchSpeed);
        }

        void EnterReturn()
        {
            SetState(GuardState.Return);
            if (waypoints != null && waypoints.Length > 0)
            {
                waypointIndex = NearestWaypointIndex();
                if (waypoints[waypointIndex] != null)
                    GoTo(waypoints[waypointIndex].position, patrolSpeed);
            }
        }

        /// <summary>
        /// 플레이어가 붙잡혀 시작 지점으로 돌아갈 때, 경비도 아무 일 없었던 상태로 되돌린다.
        /// 그러지 않으면 부활하자마자 전원이 추격 상태로 몰려온다.
        /// </summary>
        public void ResetToPatrol()
        {
            Awareness = 0f;
            SeesPlayer = false;
            targetCoin = null;
            coinDwellTimer = 0f;
            scanStep = 0;
            scanTimer = 0f;
            waitTimer = 0f;
            stateTimer = 0f;
            decayTimer = 0f;
            State = GuardState.Patrol;

            if (agent.isOnNavMesh) agent.isStopped = false;

            if (waypoints == null || waypoints.Length == 0) return;
            waypointIndex = NearestWaypointIndex();
            if (waypoints[waypointIndex] != null) GoTo(waypoints[waypointIndex].position, patrolSpeed);
        }

        void SetState(GuardState next)
        {
            if (State == next) return;
            State = next;
            stateTimer = 0f;
            if (agent.isOnNavMesh) agent.isStopped = false;
        }

        // --------------------------------------------------------------- 유틸

        /// <summary>
        /// 제자리에서 좌우로 두리번거린다. 기준 각도를 도착 시점에 한 번만 고정하는 것이 핵심이다.
        ///
        /// 예전에는 <c>transform.eulerAngles.y</c>(자기 자신의 현재 각도)에 매 프레임 오프셋을 더했다.
        /// 회전은 그 결과를 향해 움직이고 다음 프레임에 그 각도를 다시 읽으므로, 자기 출력이 입력으로
        /// 되먹임되어 각도가 계속 누적됐다. 결국 경비가 제자리에서 360도를 돌면서
        /// 등 뒤에 있던 플레이어까지 발견했다.
        /// </summary>
        void LookAround(float speed, float degrees)
        {
            if (!lookBaseSet)
            {
                lookBaseYaw = transform.eulerAngles.y;
                lookBaseSet = true;
            }

            lookTarget = Quaternion.Euler(0f, lookBaseYaw + Mathf.Sin(Time.time * speed) * degrees, 0f);
        }

        void GoTo(Vector3 destination, float speed)
        {
            // 새 목적지로 움직이기 시작하면 두리번거림의 기준 각도를 버린다.
            // 다음에 도착했을 때 그 자리에서 다시 잡는다.
            lookBaseSet = false;

            if (!agent.isOnNavMesh) return;

            agent.speed = speed;
            agent.isStopped = false;

            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                hasDestination = true;
            }
        }

        bool ReachedDestination()
        {
            if (!agent.isOnNavMesh || !hasDestination) return true;
            if (agent.pathPending) return false;
            return agent.remainingDistance <= agent.stoppingDistance + 0.35f;
        }

        Vector3 RandomPointNear(Vector3 center, float radius)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            return NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas)
                ? hit.position
                : center;
        }

        int NearestWaypointIndex()
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                float d = (waypoints[i].position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        void UpdateRotation()
        {
            Quaternion target = lookTarget;
            float turnSpeed = 420f;

            if (agent.isOnNavMesh && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f)
            {
                target = Quaternion.LookRotation(agent.velocity.normalized, Vector3.up);
            }
            else if (State == GuardState.Chase && player != null)
            {
                Vector3 dir = player.transform.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) target = Quaternion.LookRotation(dir, Vector3.up);
            }
            else if (State == GuardState.Patrol)
            {
                // 제자리에서 주변을 살필 때는 천천히 돌아야 지나갈 틈이 생긴다.
                turnSpeed = scanTurnSpeed;
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        void UpdateIndicator()
        {
            if (indicator == null) return;

            Color c = State switch
            {
                GuardState.Chase => new Color(1f, 0.2f, 0.2f),
                GuardState.Search => new Color(1f, 0.55f, 0.1f),
                GuardState.Suspicious => new Color(1f, 0.9f, 0.2f),
                GuardState.Return => new Color(0.5f, 0.8f, 1f),
                _ => new Color(0.35f, 1f, 0.6f)
            };

            float pulse = State == GuardState.Chase ? 1f + Mathf.Sin(Time.time * 12f) * 0.25f : 1f;
            indicator.localScale = Vector3.one * 0.3f * pulse;

            if (indicatorRenderer != null)
            {
                indicatorRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                indicatorRenderer.SetPropertyBlock(mpb);
            }
        }

        void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next == null) continue;
                Gizmos.DrawLine(waypoints[i].position + Vector3.up * 0.1f, next.position + Vector3.up * 0.1f);
                Gizmos.DrawWireSphere(waypoints[i].position + Vector3.up * 0.1f, 0.3f);
            }
        }
    }
}
