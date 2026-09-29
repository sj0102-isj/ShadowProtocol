using System.Collections.Generic;   // List<T>
using UnityEngine;
using UnityEngine.AI;               // NavMeshAgent — 길찾기

namespace Stealth
{
    /// <summary>
    /// 경비 AI 상태머신. 시야/소음 입력을 받아 상태를 전이하고 NavMesh로 움직인다.
    /// </summary>
    //
    // ── 이 파일을 읽는 순서 ──
    //   1) Update()        : 매 프레임 무엇을 하는지의 큰 그림
    //   2) UpdateAwareness(): 경계 게이지가 어떻게 차고 빠지는지
    //   3) TickState()     : 상태별로 갈라지는 지점
    //   4) Tick***()       : 상태 하나하나의 행동
    //   5) Enter***()      : 상태를 바꾸는 순간에만 하는 일
    //
    // 상태머신이란: "지금 어떤 상태인지"를 변수 하나(State)에 담아두고, 매 프레임 그 상태에
    // 맞는 함수만 실행하는 방식. if 문이 뒤엉키는 것을 막아준다.
    //   Patrol(순찰) → Suspicious(의심) → Search(수색) → Return(복귀) → Patrol
    //                                  ↘ Chase(발각)
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(GuardVision))]
    public class GuardAI : MonoBehaviour
    {
        // static = 클래스 전체가 공유하는 값. 경비가 몇 명이든 이 리스트는 하나뿐이다.
        // 각 경비가 OnEnable 에서 자기를 넣고 OnDisable 에서 뺀다. 그래서 "지금 씬에 있는
        // 모든 경비"를 FindObjectsByType 같은 느린 검색 없이 즉시 얻을 수 있다.
        public static readonly List<GuardAI> All = new List<GuardAI>();

        /// <summary>
        /// 동전 응답자를 고를 때 경로 길이를 재는 용도. 순차적으로만 쓰므로 하나면 된다.
        /// 필드 초기화로 만들면 Unity가 "생성자에서 NavMeshPath를 만들 수 없다"며 막으므로
        /// 처음 쓸 때 만든다.
        /// </summary>
        static NavMeshPath pathBuffer;

        // ============================================================
        //  인스펙터 설정값
        // ============================================================

        [Header("Patrol")]
        public Transform[] waypoints;       // 순찰 지점들. StageBuilder 가 만들어 꽂아준다
        public float patrolSpeed = 2.2f;    // 순찰 이동 속도(m/s)
        [Tooltip("순찰 지점 도착 후, 고개를 돌리기 전에 가만히 서 있는 시간.")]
        public float arrivePause = 1.2f;
        [Tooltip("좌/우를 확인할 때 그 방향을 바라본 채 머무는 시간.")]
        public float scanHold = 0.9f;
        [Tooltip("좌우로 확인하는 각도.")]
        public float lookAroundAngle = 65f;
        [Tooltip("제자리에서 고개를 돌리는 속도(도/초). 느릴수록 지나갈 틈이 넓어진다.")]
        public float scanTurnSpeed = 85f;
        [Tooltip("목적지로 가는 중인데 이만큼 제자리에 굳어 있으면 그 목적지를 포기한다. 경비끼리 서로를 막았을 때 푸는 장치.")]
        public float stuckTimeout = 3f;
        [Tooltip("수색 중 제자리에서 좌우로 살피는 각도. 기준 각도에서 ± 이만큼만 움직인다.")]
        public float searchLookAngle = 40f;
        [Tooltip("동전을 줍고 주위를 살피는 각도. 이 동안은 시야가 꺼져 있다.")]
        public float coinLookAngle = 30f;

        [Header("Search")]
        [Tooltip("의심한 뒤 확인하러 출발하기까지 멈춰 서 있는 시간.")]
        public float suspicionPause = 1.6f;
        public float searchSpeed = 3.4f;    // 수색 중에는 순찰보다 빠르게 움직인다
        public float searchDuration = 8f;   // 이 시간이 지나면 포기하고 순찰로 돌아간다
        public float searchRadius = 5f;     // 관심 지점 주변 이 반경 안을 헤맨다

        [Header("Detection (쉬운 난이도 기준)")]
        [Tooltip("시야 끝에서 경계가 차오르는 속도 (초당).")]
        public float baseDetectRate = 0.38f;
        [Tooltip("코앞에서 추가로 붙는 속도 (초당).")]
        public float closeDetectRate = 0.95f;
        [Tooltip("시야에서 벗어났을 때 경계가 빠지는 속도 (초당).")]
        public float decayRate = 0.5f;
        [Tooltip("시야를 벗어난 뒤 경계가 줄기 시작하기까지의 유예.")]
        public float decayDelay = 0.8f;
        [Range(0f, 1f)] public float suspiciousThreshold = 0.35f;   // 이만큼 차면 의심 상태로

        [Header("Hearing")]
        public float hearingMultiplier = 1f;   // 경비별 청력 차이를 주고 싶을 때 쓰는 배수
        [Tooltip("던진 동전 앞에서 두리번거리는 시간(초).")]
        public float coinInspectSeconds = 2.5f;

        [Header("Roam")]
        [Tooltip(
            "켜면 정해진 순찰 지점을 도는 대신 지정된 사각형 구역 안을 무작위로 돌아다닌다.\n" +
            "스테이지마다 한두 명만 이렇게 두는 것이 설계 의도다 — 전원이 배회하면 " +
            "'관찰해서 빈틈을 찾는다'는 게임의 핵심이 사라진다.")]
        public bool roam;
        public Vector3 roamCenter;   // 구역의 한가운데 (월드 좌표)
        public Vector3 roamSize;     // 구역의 가로·세로 크기

        [Header("Indicator")]
        public Transform indicator;   // 머리 위 상태 표시 큐브

        // ============================================================
        //  바깥에서 읽는 상태값
        // ============================================================

        /// <summary>지금 어떤 상태인가. = GuardState.Patrol 은 초기값 지정이다.</summary>
        public GuardState State { get; private set; } = GuardState.Patrol;

        /// <summary>경계 게이지 0~1. 1이 되면 발각.</summary>
        public float Awareness { get; private set; }

        /// <summary>이번 프레임에 플레이어가 보이는가.</summary>
        public bool SeesPlayer { get; private set; }

        // ============================================================
        //  내부 변수
        // ============================================================

        NavMeshAgent agent;             // 길찾기 부품. 목적지만 주면 알아서 벽을 피해 걸어간다
        GuardVision vision;             // 시야 판정 + 콘 그리기 부품
        PlayerController player;        // 감시 대상
        Renderer indicatorRenderer;     // 머리 위 큐브의 렌더러
        MaterialPropertyBlock mpb;      // 머티리얼을 복제하지 않고 색만 바꾸는 도구

        int waypointIndex;              // 지금 향하는 순찰 지점 번호
        int scanStep;                   // 도착 후 두리번거리기 단계 (0정지 1좌 2우 3정면)
        float scanTimer;                // 그 단계에서 머문 시간
        float stateTimer;               // 현재 상태가 된 뒤 흐른 시간
        float waitTimer;                // 수색 중 다음 지점으로 옮기기까지의 대기
        bool wasFrozen;                 // 지난 프레임에 멈춰 있었는가 (메뉴/연출 중)
        Coin targetCoin;                // 주우러 가는 동전. null 이면 없음
        float coinDwellTimer;           // 동전 앞에서 머문 시간
        float decayTimer;               // 경계가 줄기 시작하기까지 남은 유예
        Vector3 pointOfInterest;        // 확인하러 갈 지점
        Vector3 lastKnownPlayerPos;     // 마지막으로 본 플레이어 위치
        Quaternion lookTarget;          // 바라보고 싶은 방향. UpdateRotation 이 여기로 서서히 돌린다

        /// <summary>제자리에서 두리번거릴 때의 기준 각도. 도착 시점에 한 번만 고정한다.</summary>
        float lookBaseYaw;
        bool lookBaseSet;               // 기준 각도를 이미 잡았는가
        bool hasDestination;            // 목적지를 한 번이라도 준 적이 있는가
        float stuckTimer;               // 목적지가 남았는데 못 움직인 시간

        // 셰이더 변수 이름을 숫자 ID로 바꿔 저장해 둔다.
        // SetColor("_BaseColor") 처럼 문자열로 부르면 매번 이름을 해싱해야 해서 느리다.
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ============================================================
        //  유니티 생명주기
        // ============================================================

        // ── 개체별 성격 ──
        // 경비마다 다르게 보이되, 같은 경비는 항상 같아야 한다.
        // 매번 다르면 플레이어가 패턴을 못 배우고, 잡혀도 "내 실수"가 아니라 "운"이 된다.
        // 잠입 게임에서 관찰 가능성은 절대 포기할 수 없는 값이다.
        bool scanLeftFirst = true;   // 좌→우 로 볼지, 우→좌 로 볼지
        bool skipSideScan;           // 게으른 개체는 좌우를 아예 안 본다

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            vision = GetComponent<GuardVision>();
            ApplyPersonality();

            // NavMeshAgent 가 회전까지 맡으면 두리번거리기를 우리가 제어할 수 없다.
            // 회전은 직접 UpdateRotation() 에서 처리한다.
            agent.updateRotation = false;

            agent.speed = patrolSpeed;
            agent.stoppingDistance = 0.15f;   // 목적지에서 이만큼 남으면 도착으로 친다
            lookTarget = transform.rotation;
            mpb = new MaterialPropertyBlock();
            if (indicator != null) indicatorRenderer = indicator.GetComponent<Renderer>();
        }

        /// <summary>
        /// OnEnable / OnDisable — 오브젝트가 켜질 때 / 꺼질 때.
        /// 명단(All)에 등록·해제하고, 소음 알림을 구독·해지한다.
        /// 등록과 해제를 짝으로 두는 것이 중요하다. 해제를 빠뜨리면 사라진 경비가
        /// 명단에 남아 null 참조 오류가 난다.
        /// </summary>
        void OnEnable()
        {
            All.Add(this);

            // += 는 이벤트 구독. "소리가 나면 내 HandleNoise 함수를 불러달라"는 뜻이다.
            // 소리를 내는 쪽(NoiseSystem)은 누가 듣는지 몰라도 되고, 듣는 쪽은 스스로 등록만 하면 된다.
            NoiseSystem.OnNoise += HandleNoise;
        }

        void OnDisable()
        {
            All.Remove(this);
            NoiseSystem.OnNoise -= HandleNoise;   // -= 는 구독 해지
        }

        /// <summary>
        /// 순찰 습관을 경비마다 무작위로 정한다. 한 번만(Awake) 뽑으므로
        /// <b>한 판 안에서는 각 경비가 자기 습관을 계속 유지한다.</b> 매 순찰마다 바뀌면
        /// 플레이어가 아무것도 배울 수 없어서 잡혀도 자기 실수로 느껴지지 않는다.
        ///
        /// 흔드는 것은 <b>보는 순서</b>와 <b>좌우 확인 여부</b> 둘뿐이다.
        ///
        /// 대기 시간 · 고개 속도 · 시야각 · 이동 속도는 전원 같은 값으로 둔다.
        /// 그 넷은 난이도에 직접 걸리는 수치라, 흔들면 1~5 스테이지에서 시야 커버리지를
        /// 계산해 맞춰둔 밸런스가 같이 무너진다. 반면 순서와 생략은 난이도가 아니라
        /// <b>리듬</b>을 바꾸는 값이라 마음껏 흔들어도 안전하다.
        /// </summary>
        void ApplyPersonality()
        {
            // 보는 순서. 절반은 오른쪽부터 본다.
            scanLeftFirst = Random.value < 0.5f;

            // 셋 중 하나꼴로 좌우를 아예 건너뛰고 바로 다음 지점으로 간다.
            // 둘 중에 이게 훨씬 눈에 띈다 — "쟤는 뒤를 안 돌아보네"가 바로 읽힌다.
            skipSideScan = Random.value < 0.33f;
        }

        /// <summary>Start — 모든 오브젝트의 Awake 가 끝난 뒤 한 번. 남을 찾을 때는 여기서 한다.</summary>
        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();

            if (waypoints == null || waypoints.Length == 0) return;

            // ── 출발을 어긋나게 한다 ──
            // 경비는 순찰 지점 0번 위에 스폰된다. 전원이 0번을 목표로 삼으면
            //   ① 모두 "이미 도착" 상태가 되어 같은 순간에 두리번거리기를 시작하고
            //   ② 같은 순간에 끝나 ③ 다 같이 1번 지점으로 출발한다.
            // 경로가 대부분 가로 한 쌍이라, 게임을 켜면 전원이 한 덩어리로 오른쪽으로 갔다.
            //
            // 목표 지점을 무작위로 고르면 절반은 서서 살피고 절반은 바로 걷기 시작한다.
            if (roam)
            {
                GoTo(RandomPointInArea(), patrolSpeed);
            }
            else
            {
                waypointIndex = Random.Range(0, waypoints.Length);
                if (waypoints[waypointIndex] != null) GoTo(waypoints[waypointIndex].position, patrolSpeed);
            }

            // 두리번거림 박자도 어긋나게 한다. 이게 없으면 제자리에 선 경비들끼리
            // 고개를 똑같이 맞춰 돌려서 여전히 한 덩어리로 보인다.
            scanTimer = Random.Range(0f, arrivePause);
            scanStep = Random.Range(0, 4);
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;

            // 메뉴 화면이거나 붙잡힘 연출 중이면 경비도 멈춰야 한다.
            bool frozen = gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive);

            if (frozen)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                wasFrozen = true;

                // 암전이 진행되는 동안에도 붉은 콘이 보이도록 시각 갱신은 계속한다.
                vision.SetStateColor(State, Awareness);
                return;
            }

            // 멈춰 있다가 막 풀린 프레임. 다시 걷게 해준다.
            if (wasFrozen)
            {
                wasFrozen = false;
                if (agent.isOnNavMesh) agent.isStopped = false;
            }

            // 동전을 줍는 동안은 고개를 숙이고 있는 것으로 본다.
            vision.Blinded = InspectingCoin;

            // 매 프레임 이 다섯 가지를 순서대로 한다. 순서에 의미가 있다.
            UpdateAwareness();   // 1. 보이는가? 경계 게이지 갱신 (상태가 바뀔 수도 있음)
            TickState();         // 2. 지금 상태에 맞는 행동
            UpdateRotation();    // 3. 몸 돌리기
            UpdateIndicator();   // 4. 머리 위 표시
            vision.SetStateColor(State, Awareness);   // 5. 시야 콘 색
        }

        // ---------------------------------------------------------------- 인지

        /// <summary>
        /// 경계 게이지를 채우거나 뺀다. 즉시 발각이 아니라 게이지로 만든 이유는,
        /// 스쳐 지나가는 순간에 바로 잡히면 플레이어가 억울하기 때문이다.
        /// </summary>
        void UpdateAwareness()
        {
            // out float closeness — CanSee 가 "보이는가"와 "얼마나 가까운가"를 동시에 돌려준다.
            SeesPlayer = vision.CanSee(player, out float closeness);

            if (SeesPlayer)
            {
                decayTimer = decayDelay;                          // 감소 유예를 다시 채운다
                lastKnownPlayerPos = player.transform.position;   // 놓쳐도 여기로 찾아온다

                // 이미 한 번 들킨 적이 있으면 경비들이 예민해진다.
                float alertBoost = 1f;
                if (StealthGameManager.Instance != null)
                    alertBoost += StealthGameManager.Instance.AlertLevel * 0.25f;

                // 초당 차오르는 양 = (기본 + 가까움 보너스) × 플레이어 눈에 띔 정도 × 경계 배수
                float rate = (baseDetectRate + closeDetectRate * closeness)
                             * player.VisibilityMultiplier
                             * alertBoost;

                // Mathf.Min(1f, ...) 로 1을 넘지 않게 막는다.
                Awareness = Mathf.Min(1f, Awareness + rate * Time.deltaTime);

                if (Awareness >= 1f && State != GuardState.Chase)
                {
                    EnterChase();       // 게이지가 꽉 찼다 = 발각
                }
                // 복귀(Return)도 순찰과 똑같이 반응한다. 예전에는 순찰만 여기로 와서, 순찰로 돌아가던 경비는
                // 플레이어를 보고도 멈추지 않고 걸어가다가 게이지가 차는 순간 곧바로 붙잡았다.
                else if (Awareness >= suspiciousThreshold
                         && (State == GuardState.Patrol || State == GuardState.Return))
                {
                    EnterSuspicious(player.transform.position);   // 절반쯤 찼다 = 의심
                }
            }
            else
            {
                // 안 보이면: 먼저 유예 시간을 소모하고, 그다음부터 게이지가 줄어든다.
                // 유예가 없으면 기둥 뒤로 1초만 숨어도 게이지가 바로 빠져서 긴장감이 사라진다.
                if (decayTimer > 0f) decayTimer -= Time.deltaTime;
                else Awareness = Mathf.Max(0f, Awareness - decayRate * Time.deltaTime);
            }
        }

        /// <summary>
        /// 발소리 같은 보통 소음. 들은 경비는 전부 대략적인 방향으로 움직인다.
        /// 동전은 이 경로를 타지 않는다. AlertNearestToCoin 이 한 명만 골라서 직접 보낸다.
        /// </summary>
        // 이 함수는 OnEnable 에서 NoiseSystem.OnNoise 에 등록해 뒀기 때문에,
        // 누군가 소리를 내면 유니티가 아니라 NoiseSystem 이 호출해 준다.
        void HandleNoise(Vector3 position, float radius, float strength, Transform source)
        {
            if (State == GuardState.Chase) return;                            // 이미 발각이면 무의미
            if (source != null && source.GetComponent<Coin>() != null) return; // 동전은 따로 처리

            float reach = radius * hearingMultiplier;

            // 먼저 직선 거리로 싸게 거른다. 직선으로도 멀면 걸어서는 무조건 더 멀다.
            if ((transform.position - position).sqrMagnitude > reach * reach) return;

            // 그다음 실제로 걸어가는 거리. 동전(AlertNearestToCoin)과 같은 규칙이다.
            // 예전에는 직선 거리만 봐서, 벽 하나 너머 옆방 경비가 대시 소리를 듣고
            // 한참을 돌아 찾으러 왔다. 소리가 난 자리가 NavMesh 밖이면(소품 옆 등) 가장 가까운 바닥으로 맞춘다.
            if (NavMesh.SamplePosition(position, out NavMeshHit heard, 2f, NavMesh.AllAreas)
                && WalkDistance(transform.position, heard.position, reach) > reach) return;

            // 발소리로는 방향만 어렴풋이 안다. 정확한 자리를 집어주면 너무 정확해진다.
            // insideUnitSphere = 반지름 1인 구 안의 임의의 점. 곱해서 오차 범위를 만든다.
            Vector3 guess = position + Random.insideUnitSphere * 1.5f;
            guess.y = position.y;   // 위아래 오차는 빼준다. 바닥 위에서만 움직이므로

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
        // static 인 이유: 특정 경비 하나의 일이 아니라 "전체 중에서 한 명을 고르는" 일이다.
        // 그래서 Coin 이 GuardAI.AlertNearestToCoin(...) 처럼 클래스 이름으로 직접 부른다.
        public static void AlertNearestToCoin(Vector3 position, float radius, Coin coin)
        {
            // 동전이 벽에 끼거나 소품 위에 올라앉았어도 경비가 갈 수 있는 자리로 맞춘다.
            // SamplePosition = "이 좌표에서 가장 가까운 NavMesh 위의 점"을 찾아준다.
            if (!NavMesh.SamplePosition(position, out NavMeshHit landing, 4f, NavMesh.AllAreas)) return;

            GuardAI nearest = null;
            float best = float.MaxValue;   // "아직 아무도 못 찾음"을 뜻하는 초기값

            for (int i = 0; i < All.Count; i++)
            {
                GuardAI guard = All[i];
                if (guard == null || !guard.CanRespondToCoin) continue;

                float reach = radius * guard.hearingMultiplier;

                // 걸어가는 거리는 직선 거리보다 항상 길다. 먼저 싸게 거른다.
                // (직선으로도 멀면 걸어서는 무조건 더 멀다 → 경로 계산을 할 필요조차 없다)
                if ((guard.transform.position - landing.position).sqrMagnitude > reach * reach) continue;

                float walk = WalkDistance(guard.transform.position, landing.position, reach);
                if (walk >= best) continue;   // 지금까지 찾은 사람보다 멀면 탈락

                nearest = guard;
                best = walk;
            }

            if (nearest == null) return;   // 들은 사람이 아무도 없다

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
            // ??= 는 "왼쪽이 null 이면 오른쪽을 대입". null 병합 할당 연산자.
            pathBuffer ??= new NavMeshPath();

            // CalculatePath 는 실제로 이동시키지 않고 경로만 계산해 준다.
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, pathBuffer)) return float.MaxValue;

            // PathComplete 가 아니면 중간에 끊긴 경로다. 갈 수 없다는 뜻.
            if (pathBuffer.status != NavMeshPathStatus.PathComplete) return float.MaxValue;

            // corners = 경로의 꺾이는 점들. 이 점들 사이 거리를 전부 더하면 총 이동 거리다.
            Vector3[] corners = pathBuffer.corners;
            float total = 0f;

            for (int i = 1; i < corners.Length; i++)
            {
                total += Vector3.Distance(corners[i - 1], corners[i]);
                if (total > limit) return float.MaxValue;   // 넘는 순간 중단 (조기 종료)
            }

            return total;
        }

        /// <summary>동전 앞에 도착해 줍고 있는 중인가.</summary>
        bool InspectingCoin => targetCoin != null && coinDwellTimer > 0f;

        /// <summary>순찰·복귀 중인 경비만 동전을 주우러 갈 수 있다.</summary>
        bool CanRespondToCoin => State == GuardState.Patrol || State == GuardState.Return;

        // ------------------------------------------------------------ 상태머신

        /// <summary>상태에 맞는 함수로 갈라준다. 상태머신의 심장.</summary>
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
                scanStep = 0;     // 아직 걸어가는 중 — 두리번거리기 단계를 초기화
                scanTimer = 0f;

                // 못 가고 굳었으면 이 지점은 버리고 다음 지점으로 넘어간다.
                if (StuckTooLong()) AdvanceWaypoint();
                return;
            }

            stuckTimer = 0f;

            // 기준 각도는 도착했을 때 한 번만 잡는다. 매 프레임 현재 각도를 다시 읽으면
            // 자기 출력이 입력으로 되먹임되어 회전이 누적된다.
            if (!lookBaseSet)
            {
                // 배회형은 매번 목적지가 달라서 순찰 지점의 방향이 의미가 없다.
                // 걸어온 방향(= 지금 보고 있는 쪽)을 그대로 기준으로 삼는다.
                lookBaseYaw = !roam && waypoints[waypointIndex] != null
                    ? waypoints[waypointIndex].eulerAngles.y   // 순찰 지점이 가진 방향을 기준으로
                    : transform.eulerAngles.y;
                lookBaseSet = true;
            }

            // 게으른 개체는 0단계(정지)만 하고 바로 다음 지점으로 간다.
            if (skipSideScan && scanStep >= 1)
            {
                scanStep = 0;
                scanTimer = 0f;
                AdvanceWaypoint();
                return;
            }

            // 단계별로 기준에서 얼마나 틀 것인가.
            // scanLeftFirst 가 false 인 개체는 부호가 뒤집혀 오른쪽부터 본다.
            float side = scanLeftFirst ? 1f : -1f;
            float offset = scanStep switch
            {
                1 => -lookAroundAngle * side,   // 먼저 보는 쪽
                2 => lookAroundAngle * side,    // 나중에 보는 쪽
                _ => 0f                         // 0단계와 3단계는 정면
            };
            lookTarget = Quaternion.Euler(0f, lookBaseYaw + offset, 0f);

            // 아직 그 방향을 보고 있지 않다면 대기 시간을 세지 않는다.
            // 이 한 줄 덕분에 "돌면서 지나간다"가 아니라 "돌고 나서 멈춰 본다"가 된다.
            if (Quaternion.Angle(transform.rotation, lookTarget) > 6f) return;

            scanTimer += Time.deltaTime;

            float hold = scanStep switch
            {
                0 => arrivePause,   // 도착 직후
                3 => 0.4f,          // 정면 복귀는 짧게
                _ => scanHold       // 좌우 확인
            };
            if (scanTimer < hold) return;

            // 한 단계 끝 — 다음 단계로
            scanTimer = 0f;
            scanStep++;

            if (scanStep <= 3) return;

            // 네 단계를 다 마쳤으면 다음 순찰 지점으로 출발
            scanStep = 0;
            AdvanceWaypoint();
        }

        /// <summary>다음 목적지로 출발한다. 배회형이면 구역 안의 새 지점을 뽑는다.</summary>
        void AdvanceWaypoint()
        {
            if (roam)
            {
                GoTo(RandomPointInArea(), patrolSpeed);
                return;
            }

            // % 는 나머지 연산. 마지막 지점 다음이 다시 0번이 되어 순환한다.
            waypointIndex = (waypointIndex + 1) % waypoints.Length;

            if (waypoints[waypointIndex] != null)
                GoTo(waypoints[waypointIndex].position, patrolSpeed);
        }

        /// <summary>
        /// 배회 구역 안에서 갈 수 있는 지점을 하나 고른다.
        ///
        /// 너무 가까운 곳을 고르면 제자리에서 맴도는 것처럼 보이므로, 지금 위치에서
        /// 일정 거리 이상 떨어진 곳을 우선으로 찾는다. 몇 번 실패하면 조건을 버리고
        /// 아무 데나 고른다 — 자리를 못 찾아 멈춰 서는 것보다는 낫다.
        /// </summary>
        Vector3 RandomPointInArea()
        {
            Vector3 fallback = transform.position;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                var candidate = new Vector3(
                    roamCenter.x + Random.Range(-roamSize.x, roamSize.x) * 0.5f,
                    roamCenter.y,
                    roamCenter.z + Random.Range(-roamSize.z, roamSize.z) * 0.5f);

                // 뽑은 점이 벽 안일 수 있으므로 갈 수 있는 자리로 당긴다.
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;

                fallback = hit.position;

                // 처음 8번은 "충분히 먼 곳"만 받는다. 그 뒤로는 조건을 푼다.
                if (attempt < 8 && (hit.position - transform.position).sqrMagnitude < 16f) continue;

                return hit.position;
            }

            return fallback;
        }

        /// <summary>의심 상태. 멈춰 서서 그쪽을 노려보다가, 시간이 지나면 확인하러 간다.</summary>
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

        /// <summary>수색 상태. 관심 지점 주변을 임의로 돌아다니며 살핀다.</summary>
        void TickSearch()
        {
            // 동전을 주우러 가는 중이면 그쪽 로직으로 완전히 갈라진다.
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
                LookAround(2f, searchLookAngle);   // 제자리에서 좌우로 훑는다

                if (waitTimer <= 0f)
                {
                    waitTimer = 1.2f;
                    // 주변 임의의 지점으로 옮겨가며 뒤진다
                    GoTo(RandomPointNear(pointOfInterest, searchRadius), searchSpeed);
                }
            }

            if (stateTimer > searchDuration) EnterReturn();   // 시간이 다 되면 포기
        }

        /// <summary>
        /// 던져진 동전을 확인하러 가는 동작. 주우면 잠깐 두리번거린 뒤 순찰로 돌아간다.
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
            // sqrMagnitude < 4f 는 "거리 2m 이내"와 같은 뜻이다 (2의 제곱이 4).
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

        /// <summary>복귀 상태. 가장 가까운 순찰 지점까지 걸어가면 순찰로 되돌아간다.</summary>
        void TickReturn()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                SetState(GuardState.Patrol);
                return;
            }

            // 복귀도 도착해야만 끝난다. 막혔으면 그 자리에서 순찰을 다시 시작한다.
            if (ReachedDestination() || StuckTooLong()) SetState(GuardState.Patrol);
        }

        // --------------------------------------------------------------- 전이
        //
        // Enter*** 함수들은 "상태가 바뀌는 그 순간에 딱 한 번" 해야 할 일을 모아둔 곳이다.
        // 매 프레임 하는 일(Tick***)과 분리해 두면 흐름을 읽기가 훨씬 쉬워진다.

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

            gm.ReportDetection();   // 경계 수치를 올려 다음부터 더 예민해지게
            gm.Capture();           // 붙잡힘 연출 시작
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
                // 원래 순서의 다음 지점이 아니라 "지금 제일 가까운" 지점으로 돌아간다.
                // 그래야 수색하다 멀리 온 경비가 맵을 가로질러 되돌아가지 않는다.
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

        /// <summary>상태를 바꾸는 유일한 통로. 여기를 거치게 하면 타이머 초기화를 빠뜨릴 수 없다.</summary>
        void SetState(GuardState next)
        {
            if (State == next) return;   // 같은 상태면 아무것도 안 한다 (타이머가 계속 0이 되는 것 방지)
            State = next;
            stateTimer = 0f;
            if (agent.isOnNavMesh) agent.isStopped = false;

            // 상태가 바뀌는 이 한 곳만 잡으면 경비 관련 효과음이 전부 처리된다.
            // (색이 바뀌는 순간과 소리가 나는 순간이 자동으로 일치한다)
            switch (next)
            {
                case GuardState.Suspicious: GameAudio.Play(Sfx.Suspicious); break;
                case GuardState.Search:     GameAudio.Play(Sfx.Search);     break;
                case GuardState.Chase:      GameAudio.Play(Sfx.Detected);   break;
            }
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

            // Mathf.Sin 은 -1과 1 사이를 부드럽게 오간다. 시간을 넣으면 좌우로 흔들리는 값이 된다.
            // speed 를 곱하면 흔들리는 빠르기, degrees 를 곱하면 흔들리는 폭이 된다.
            lookTarget = Quaternion.Euler(0f, lookBaseYaw + Mathf.Sin(Time.time * speed) * degrees, 0f);
        }

        /// <summary>목적지를 지정한다. 상태가 바뀔 때마다 여기를 거친다.</summary>
        void GoTo(Vector3 destination, float speed)
        {
            // 새 목적지로 움직이기 시작하면 두리번거림의 기준 각도를 버린다.
            // 다음에 도착했을 때 그 자리에서 다시 잡는다.
            lookBaseSet = false;

            // NavMesh 위에 있지 않으면 길찾기 명령이 예외를 던진다. 먼저 막는다.
            if (!agent.isOnNavMesh) return;

            agent.speed = speed;
            agent.isStopped = false;

            // 목적지가 길 밖이면 가장 가까운 길 위의 점으로 보정한다.
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);   // 실제 이동 명령은 이 한 줄
                hasDestination = true;
            }
        }

        /// <summary>목적지에 도착했는가.</summary>
        /// <summary>
        /// 목적지가 남았는데 제자리에 굳어 있는가.
        ///
        /// 경비 둘이 같은 칸을 목적지로 삼으면 서로를 밀어내다가 둘 다 도착하지 못한다.
        /// remainingDistance 는 끝내 줄지 않는데 순찰은 <b>도착해야만</b> 다음 지점으로
        /// 넘어가므로, 그 자리에서 영원히 굳는다. 실제로 무한모드 중앙 열람홀에서
        /// 네 명이 모서리를 공유하고 있어 두 명씩 붙어 선 채 멈춰 있었다.
        ///
        /// 원인을 하나씩 막는 대신 결과를 본다 — 무엇 때문이든 못 가면 목적지를 버린다.
        /// 문틀에 끼든, 베이크 경계에 걸리든, 경비끼리 막든 똑같이 풀린다.
        /// </summary>
        bool StuckTooLong()
        {
            // 경로를 아직 계산 중이거나 초당 0.2m 이상 움직이고 있으면 막힌 게 아니다.
            if (agent.pathPending || agent.velocity.sqrMagnitude > 0.04f)
            {
                stuckTimer = 0f;
                return false;
            }

            stuckTimer += Time.deltaTime;
            if (stuckTimer < stuckTimeout) return false;

            stuckTimer = 0f;
            return true;
        }

        bool ReachedDestination()
        {
            if (!agent.isOnNavMesh || !hasDestination) return true;

            // pathPending = 경로를 아직 계산 중. 이때 remainingDistance 는 0이라 도착으로 오인된다.
            if (agent.pathPending) return false;

            // +0.35f 여유를 두는 이유: 딱 맞게 재면 미세하게 못 미쳐 영영 도착 판정이 안 나기도 한다.
            return agent.remainingDistance <= agent.stoppingDistance + 0.35f;
        }

        /// <summary>중심에서 반경 안의 임의의 지점. 수색할 때 다음에 뒤질 곳을 고른다.</summary>
        Vector3 RandomPointNear(Vector3 center, float radius)
        {
            // insideUnitCircle = 반지름 1인 원 안의 임의의 점(2D). 반경을 곱해 크기를 맞춘다.
            Vector2 offset = Random.insideUnitCircle * radius;

            // 2D 원의 y 를 3D 의 z 로 옮긴다. 바닥 평면이 XZ 이기 때문이다.
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

            // 그 지점이 벽 안이면 갈 수 있는 자리로 보정하고, 그마저 실패하면 중심을 쓴다.
            return NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas)
                ? hit.position
                : center;
        }

        /// <summary>지금 위치에서 가장 가까운 순찰 지점의 번호.</summary>
        int NearestWaypointIndex()
        {
            int best = 0;
            float bestDist = float.MaxValue;

            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;

                // 거리 비교만 할 것이므로 제곱근을 생략한 sqrMagnitude 로 충분하다.
                float d = (waypoints[i].position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }

            return best;
        }

        /// <summary>
        /// 몸을 어느 쪽으로 돌릴지 정하고 실제로 돌린다.
        /// 상황마다 보는 대상이 다르므로 우선순위를 두고 하나를 고른다.
        /// </summary>
        void UpdateRotation()
        {
            Quaternion target = lookTarget;   // 기본값: 상태 로직이 정해둔 방향
            float turnSpeed = 420f;

            // 1순위 — 움직이는 중이면 가는 방향을 본다
            if (agent.isOnNavMesh && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f)
            {
                target = Quaternion.LookRotation(agent.velocity.normalized, Vector3.up);
            }
            // 2순위 — 발각 상태면 플레이어를 가리킨다
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

            // RotateTowards = 목표 각도로 초당 turnSpeed 도씩 다가간다. 즉시 돌지 않아 자연스럽다.
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        /// <summary>머리 위 큐브의 색과 크기로 상태를 알려준다.</summary>
        void UpdateIndicator()
        {
            if (indicator == null) return;

            Color c = State switch
            {
                GuardState.Chase => new Color(1f, 0.2f, 0.2f),      // 빨강
                GuardState.Search => new Color(1f, 0.55f, 0.1f),    // 주황
                GuardState.Suspicious => new Color(1f, 0.9f, 0.2f), // 노랑
                GuardState.Return => new Color(0.5f, 0.8f, 1f),     // 하늘
                _ => new Color(0.35f, 1f, 0.6f)                     // 초록 (순찰)
            };

            // 발각 상태일 때만 크기를 두근거리게 해서 눈에 확 띄게 한다.
            float pulse = State == GuardState.Chase ? 1f + Mathf.Sin(Time.time * 12f) * 0.25f : 1f;
            indicator.localScale = Vector3.one * 0.3f * pulse;

            if (indicatorRenderer != null)
            {
                // MaterialPropertyBlock = 머티리얼을 복제하지 않고 이 렌더러의 색만 바꾸는 방법.
                // renderer.material.color = ... 로 쓰면 유니티가 머티리얼 사본을 만들어서,
                // 경비가 10명이면 머티리얼도 10개가 생긴다. 이 방식은 사본을 만들지 않는다.
                indicatorRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                indicatorRenderer.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// OnDrawGizmos — 씬 뷰에 항상 그려지는 보조선(선택하지 않아도 보인다).
        /// 순찰 경로를 선으로 이어 그려서 배치를 눈으로 확인할 수 있게 한다.
        /// </summary>
        void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;

                // % 로 마지막 지점과 첫 지점도 이어준다 (순환 경로)
                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next == null) continue;

                Gizmos.DrawLine(waypoints[i].position + Vector3.up * 0.1f, next.position + Vector3.up * 0.1f);
                Gizmos.DrawWireSphere(waypoints[i].position + Vector3.up * 0.1f, 0.3f);
            }
        }
    }
}
