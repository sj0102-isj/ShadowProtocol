using System;                          // Action<T> (이벤트)
using System.Collections.Generic;      // List<T>
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;     // 씬 로드

namespace Stealth
{
    /// <summary>미션이 지금 어느 국면인가.</summary>
    public enum MissionState { Menu, Playing, Success, Failed }

    /// <summary>
    /// 미션 전체 상태: 목표물 수집, 경보 단계, 성공/실패, 재시작, 랭크 집계.
    /// Menu 상태에서는 timeScale 이 0이라 플레이어와 경비가 자동으로 멈춘다.
    /// </summary>
    //
    // Time.timeScale = 게임 시간의 배속. 1이 정상, 0이면 완전 정지.
    // 이걸 0으로 만들면 Time.deltaTime 이 0이 되어 모든 이동·타이머가 저절로 멈춘다.
    // 스크립트마다 "지금 멈춰야 하나?"를 검사하지 않아도 되는 아주 싼 일시정지 방법이다.
    // 단, 멈춘 상태에서도 움직여야 하는 것(암전 연출, UI)은 Time.unscaledDeltaTime 을 쓴다.
    public class StealthGameManager : MonoBehaviour
    {
        public static StealthGameManager Instance { get; private set; }   // 싱글턴

        [Header("Mission")]
        public string stageName = "STAGE 01 · 창고";
        [Tooltip("0이면 씬에 있는 Objective 개수를 자동으로 센다.")]
        public int requiredObjectives = 0;
        [Tooltip("무한모드. 탈출 조건이 없고 목숨이 다할 때까지 자료를 모아 점수를 쌓는다.")]
        public bool endlessMode;

        [Header("Capture / Alert")]
        [Tooltip("발각될 때마다 시작 지점으로 돌아간다. 이 횟수에 도달하면 미션 실패.")]
        public int maxCaptures = 3;
        [Tooltip("붙잡힌 뒤 화면에 복귀 안내를 띄우는 시간(초).")]
        public float respawnNoticeSeconds = 2.4f;

        [Header("Capture Fade")]
        [Tooltip("발각 후 화면이 어두워지는 시간(초).")]
        public float fadeOutSeconds = 0.40f;
        [Tooltip("완전히 어두운 상태로 머무는 시간. 이때 시작 지점으로 옮겨진다.")]
        public float holdBlackSeconds = 0.30f;
        [Tooltip("다시 밝아지는 시간(초).")]
        public float fadeInSeconds = 0.45f;

        // ============================================================
        //  바깥에서 읽는 상태값
        // ============================================================

        public MissionState State { get; private set; } = MissionState.Menu;
        public int Collected { get; private set; }        // 모은 점수(자료 점수의 합)
        public int Required { get; private set; }         // 탈출에 필요한 수
        public int DetectionCount { get; private set; }   // 발각당한 횟수 (랭크용)
        public float ElapsedTime { get; private set; }    // 플레이 시간
        public bool AllObjectivesCollected => Collected >= Required;

        /// <summary>붙잡힌 횟수. 그대로 경보 단계가 되고, maxCaptures 에 닿으면 실패.</summary>
        public int CaptureCount { get; private set; }

        /// <summary>경보 단계 = 붙잡힌 횟수. 올라갈수록 경비가 더 빨리 알아챈다.</summary>
        public int AlertLevel => CaptureCount;

        /// <summary>0보다 크면 "붙잡혔다 — 복귀" 안내를 표시하는 중.</summary>
        public float RespawnNotice { get; private set; }

        /// <summary>암전 정도. 0 = 정상, 1 = 완전히 검은 화면.</summary>
        public float CaptureFade { get; private set; }

        /// <summary>발각 연출이 진행 중인가. 이 동안 플레이어와 경비는 멈춘다.</summary>
        public bool CaptureSequenceActive { get; private set; }

        float captureTimer;      // 연출이 시작되고 흐른 시간
        bool respawnApplied;     // 이번 연출에서 이미 순간이동시켰는가

        Vector3 playerSpawnPosition;     // 시작 지점. Start 에서 기록해 둔다
        Quaternion playerSpawnRotation;
        PlayerController player;

        /// <summary>한 번이라도 플레이가 시작됐는가. 스테이지 선택에서 재로드 여부를 판단한다.</summary>
        public bool MissionStarted { get; private set; }

        // 이벤트. 미션이 끝나면 여기 등록해 둔 함수들이 호출된다.
        // EndlessMode 가 이걸 구독해서 최고 점수를 저장한다.
        public event Action<MissionState> OnMissionEnded;

        readonly List<Objective> objectives = new List<Objective>();

        void Awake()
        {
            Instance = this;
            // 여기서 NoiseSystem.Clear() 를 부르지 않는다. 오브젝트마다 Awake·OnEnable 이 차례로 돌기 때문에,
            // 이 매니저보다 먼저 깨어난 경비가 OnEnable 에서 이미 소리를 구독했을 수 있다.
            // 그걸 지우면 그 경비는 이번 판 내내 발소리·대시를 못 듣는다(R/F5 재시작 뒤에 드러났다).
            // 옛 씬의 구독자는 각자 OnDisable 에서 스스로 빠지므로 따로 지울 필요도 없다.

            // 재시작이나 스테이지 선택으로 들어왔다면 타이틀을 건너뛰고 바로 시작한다.
            if (GameSession.SkipTitle)
            {
                GameSession.SkipTitle = false;   // 한 번만 쓰고 끈다
                State = MissionState.Playing;
                MissionStarted = true;
                Time.timeScale = 1f;
            }
            else
            {
                State = MissionState.Menu;
                Time.timeScale = 0f;   // 타이틀 화면에서는 게임 시간을 멈춰 둔다
            }
        }

        void Start()
        {
            objectives.Clear();
            // using System; 때문에 'Object'가 모호해지므로 한정자 없이 상속된 정적 메서드를 쓴다.
            //   (System.Object 와 UnityEngine.Object 가 둘 다 'Object' 라 컴파일러가 못 고른다)
            objectives.AddRange(FindObjectsByType<Objective>(FindObjectsSortMode.None));

            // requiredObjectives 가 0이면 씬에 실제로 있는 개수를 쓴다.
            // 인스펙터에서 숫자를 손으로 맞추지 않아도 되니 씬을 다시 만들어도 안 깨진다.
            Required = requiredObjectives > 0 ? requiredObjectives : objectives.Count;

            player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                // 시작 위치를 기록해 둔다. 붙잡히면 여기로 되돌린다.
                playerSpawnPosition = player.transform.position;
                playerSpawnRotation = player.transform.rotation;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // 여기서도 Clear 하지 않는다. 씬을 다시 열 때 옛 매니저의 OnDestroy 가 새 씬의 경비가
            // 구독을 마친 뒤에 도는 경우가 있어서(ExitZone.OnDestroy 참고), 새 경비의 귀를 막아 버린다.
        }

        void Update()
        {
            // 붙잡힘 연출 중에는 그것만 처리하고 끝낸다.
            if (CaptureSequenceActive)
            {
                TickCaptureSequence();
                return;
            }

            if (State == MissionState.Playing)
            {
                ElapsedTime += Time.deltaTime;
                if (RespawnNotice > 0f) RespawnNotice -= Time.deltaTime;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            bool finished = State == MissionState.Success || State == MissionState.Failed;
            if (finished && kb.rKey.wasPressedThisFrame) Restart();          // 결과 화면에서 R
            if (State != MissionState.Menu && kb.f5Key.wasPressedThisFrame) Restart();  // 언제든 F5
        }

        // ------------------------------------------------------------ 흐름

        /// <summary>타이틀에서 시작을 눌렀을 때.</summary>
        public void StartMission()
        {
            if (State != MissionState.Menu) return;
            State = MissionState.Playing;
            MissionStarted = true;
            ElapsedTime = 0f;
            Time.timeScale = 1f;
        }

        /// <summary>결과 화면에서 스테이지 선택으로 돌아갈 때. 플레이 중 ESC 일시정지도 이걸 쓴다.</summary>
        public void OpenMenu()
        {
            State = MissionState.Menu;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// 일시정지에서 돌아온다. StartMission 과 달리 경과 시간을 초기화하지 않는다.
        /// 아직 한 번도 시작하지 않았다면(타이틀 화면) 아무것도 하지 않는다.
        /// </summary>
        public void Resume()
        {
            if (State != MissionState.Menu || !MissionStarted) return;
            State = MissionState.Playing;
            Time.timeScale = 1f;
        }

        /// <summary>같은 씬을 처음부터 다시 연다.</summary>
        public void Restart()
        {
            // 씬을 다시 로드하기 전에 timeScale 을 1로 돌려놔야 한다.
            // 0인 채로 로드하면 새 씬도 멈춘 상태로 시작한다.
            Time.timeScale = 1f;
            GameSession.SkipTitle = true;   // 새 씬의 Awake 가 이걸 보고 타이틀을 건너뛴다

            // buildIndex = 지금 씬의 번호. 이름 대신 번호로 같은 씬을 다시 연다.
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>선택 화면에서 다른 스테이지로 이동한다.</summary>
        public void LoadStage(StageEntry entry)
        {
            if (!StageCatalog.IsSelectable(entry)) return;

            Time.timeScale = 1f;
            // NoiseSystem.Clear() 를 부르지 않는다. 타이틀에서 지금 열린 스테이지를 고르면 씬을 다시 열지 않고
            // 그대로 시작하는데(아래), 이때 지우면 이미 구독해 둔 경비 전원이 그 판 내내 대시 소리를 못 들었다.
            // (1스테이지를 타이틀에서 시작하면 경비 5명, 구독 0 — 실제로 재어 보고 찾았다)
            GameSession.SkipTitle = true;

            if (entry.sceneName == SceneManager.GetActiveScene().name && !MissionStarted)
            {
                // 아직 아무것도 진행되지 않은 새 씬이라면 재로드 없이 바로 시작한다.
                // 몇 초짜리 로딩을 아끼는 최적화다.
                GameSession.SkipTitle = false;
                StartMission();
                return;
            }

            SceneManager.LoadScene(entry.sceneName);
        }

        // ------------------------------------------------------------ 미션

        /// <summary>자료를 회수했을 때 Objective 가 호출한다.</summary>
        public void CollectObjective(Objective objective)
        {
            if (State != MissionState.Playing) return;

            // 자료마다 점수가 다르다(중앙 1급 기밀은 5점). null 이면 안전하게 1로 친다.
            Collected += objective != null ? objective.points : 1;

            // 무한모드에서는 회수한 자료가 다른 자리에서 되살아난다.
            if (endlessMode && EndlessMode.Instance != null)
                EndlessMode.Instance.OnObjectiveCollected(objective);
        }

        /// <summary>경비가 플레이어를 완전히 발각했을 때 호출. 랭크 평가에만 쓰인다.</summary>
        public void ReportDetection()
        {
            if (State != MissionState.Playing) return;
            DetectionCount++;
        }

        /// <summary>
        /// 경비에게 발각됐을 때. 추격 없이 곧바로 붙잡힌 것으로 처리한다.
        /// 화면이 어두워지는 동안 시작 지점으로 옮겨지고, 회수한 자료는 그대로 유지된다.
        /// maxCaptures 에 도달하면 그때 미션 실패.
        /// </summary>
        public void Capture()
        {
            // 이미 연출 중이면 무시한다. 경비 여럿이 동시에 발각하면 중복 호출되기 때문이다.
            if (State != MissionState.Playing || CaptureSequenceActive) return;

            CaptureCount++;
            GameAudio.Play(Sfx.Capture);

            if (CaptureCount >= maxCaptures)
            {
                State = MissionState.Failed;
                Time.timeScale = 0f;
                GameAudio.Play(Sfx.Fail);
                OnMissionEnded?.Invoke(State);   // 구독자(EndlessMode)에게 알린다
                return;
            }

            CaptureSequenceActive = true;
            captureTimer = 0f;
            respawnApplied = false;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// 암전 -> 시작 지점으로 이동 -> 밝아짐.
        /// timeScale 을 0으로 두고 멈춘 화면 위에서 진행하므로 unscaled 시간을 쓴다.
        /// </summary>
        //
        // 연출을 코루틴이 아니라 타이머 하나로 만든 이유: 상태가 CaptureFade 값 하나로 드러나서
        // HUD 가 그 값만 읽으면 되고, 중간에 취소하거나 되돌리기도 쉽다.
        void TickCaptureSequence()
        {
            captureTimer += Time.unscaledDeltaTime;

            // 1구간 — 어두워지는 중
            if (captureTimer < fadeOutSeconds)
            {
                CaptureFade = captureTimer / Mathf.Max(0.01f, fadeOutSeconds);
                return;
            }

            CaptureFade = 1f;

            // 완전히 어두워진 순간에 옮긴다. 순간이동하는 장면이 보이지 않도록.
            if (!respawnApplied)
            {
                respawnApplied = true;
                RespawnPlayer();
            }

            // 2구간 — 검은 화면 유지
            float afterFadeOut = captureTimer - fadeOutSeconds;
            if (afterFadeOut < holdBlackSeconds) return;

            // 3구간 — 다시 밝아지는 중
            float fadeIn = afterFadeOut - holdBlackSeconds;
            if (fadeIn < fadeInSeconds)
            {
                CaptureFade = 1f - fadeIn / Mathf.Max(0.01f, fadeInSeconds);
                return;
            }

            // 끝 — 원래대로
            CaptureFade = 0f;
            CaptureSequenceActive = false;
            Time.timeScale = 1f;
        }

        /// <summary>플레이어를 시작 지점으로 되돌리고, 경비와 드론의 경계를 지운다.</summary>
        void RespawnPlayer()
        {
            RespawnNotice = respawnNoticeSeconds;

            if (player != null) player.Teleport(playerSpawnPosition, playerSpawnRotation);

            // 카메라도 같이 옮긴다. 이 연출 동안 timeScale 이 0이라 카메라가 따라가지 못하다가,
            // 화면이 밝아진 뒤 붙잡힌 자리에서 시작 지점까지 맵을 가로질러 날아갔다.
            var follow = Camera.main != null ? Camera.main.GetComponent<IsoCameraFollow>() : null;
            if (follow != null) follow.Snap();

            // 경비를 원래대로 돌려놓지 않으면 복귀 직후 전원이 발각 상태로 남는다.
            foreach (var guard in GuardAI.All)
            {
                if (guard != null) guard.ResetToPatrol();
            }

            // 드론도 같이 지운다. 빠뜨리면 경계 게이지가 남아 복귀 직후 즉시 다시 잡힌다.
            foreach (var drone in SurveillanceDrone.All)
            {
                if (drone != null) drone.ResetScan();
            }
        }

        /// <summary>탈출 성공. ExitZone 이 호출한다.</summary>
        public void CompleteMission()
        {
            if (State != MissionState.Playing) return;

            State = MissionState.Success;
            Time.timeScale = 0f;
            GameAudio.Play(Sfx.Success);

            StageProgress.MarkCleared(SceneManager.GetActiveScene().name, EvaluateRank());
            OnMissionEnded?.Invoke(State);
        }

        /// <summary>
        /// S / A / B / C 랭크. 몇 번 들켰는가만 본다.
        /// 잠입 게임에서 시간을 걸면 신중하게 플레이한 쪽이 손해를 보므로 시간은 보지 않는다.
        ///
        /// maxCaptures 가 3이라 성공한 판의 붙잡힘 횟수는 0 · 1 · 2 뿐이고 S · A · B 가 그 셋에 맞는다.
        /// C 는 세 번 붙잡혀 실패한 경우다. 클리어가 아니므로 StageProgress 에 저장하지 않는다 —
        /// MarkCleared 는 CompleteMission 에서만 부르기 때문에 저절로 그렇게 된다.
        ///
        /// 이전에는 DetectionCount 로 S 와 A 를 갈랐는데 A 가 나올 수 없었다.
        /// 발각과 붙잡힘이 항상 같이 일어나기 때문이다 — GuardAI.EnterChase 와
        /// SurveillanceDrone.Detect 가 ReportDetection 과 Capture 를 함께 부른다.
        /// 그래서 두 카운트가 언제나 같고, "들켰지만 안 잡힘" 상태가 생기지 않는다.
        /// </summary>
        public string EvaluateRank()
        {
            if (CaptureCount >= maxCaptures) return "C";   // 세 번 붙잡혀 실패
            if (CaptureCount == 0) return "S";             // 한 번도 안 들킴
            if (CaptureCount == 1) return "A";             // 한 번 들킴
            return "B";                                    // 두 번 들킴
        }
    }
}
