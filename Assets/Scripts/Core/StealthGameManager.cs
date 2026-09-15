using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Stealth
{
    public enum MissionState { Menu, Playing, Success, Failed }

    /// <summary>
    /// 미션 전체 상태: 목표물 수집, 경보 단계, 성공/실패, 재시작, 랭크 집계.
    /// Menu 상태에서는 timeScale 이 0이라 플레이어와 경비가 자동으로 멈춘다.
    /// </summary>
    public class StealthGameManager : MonoBehaviour
    {
        public static StealthGameManager Instance { get; private set; }

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

        public MissionState State { get; private set; } = MissionState.Menu;
        public int Collected { get; private set; }
        public int Required { get; private set; }
        public int DetectionCount { get; private set; }
        public float ElapsedTime { get; private set; }
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

        float captureTimer;
        bool respawnApplied;

        Vector3 playerSpawnPosition;
        Quaternion playerSpawnRotation;
        PlayerController player;

        /// <summary>한 번이라도 플레이가 시작됐는가. 스테이지 선택에서 재로드 여부를 판단한다.</summary>
        public bool MissionStarted { get; private set; }

        public event Action<MissionState> OnMissionEnded;

        readonly List<Objective> objectives = new List<Objective>();

        void Awake()
        {
            Instance = this;
            NoiseSystem.Clear();

            // 재시작이나 스테이지 선택으로 들어왔다면 타이틀을 건너뛰고 바로 시작한다.
            if (GameSession.SkipTitle)
            {
                GameSession.SkipTitle = false;
                State = MissionState.Playing;
                MissionStarted = true;
                Time.timeScale = 1f;
            }
            else
            {
                State = MissionState.Menu;
                Time.timeScale = 0f;
            }
        }

        void Start()
        {
            objectives.Clear();
            // using System; 때문에 'Object'가 모호해지므로 한정자 없이 상속된 정적 메서드를 쓴다.
            objectives.AddRange(FindObjectsByType<Objective>(FindObjectsSortMode.None));
            Required = requiredObjectives > 0 ? requiredObjectives : objectives.Count;

            player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                playerSpawnPosition = player.transform.position;
                playerSpawnRotation = player.transform.rotation;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            NoiseSystem.Clear();
        }

        void Update()
        {
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
            if (finished && kb.rKey.wasPressedThisFrame) Restart();
            if (State != MissionState.Menu && kb.f5Key.wasPressedThisFrame) Restart();
        }

        // ------------------------------------------------------------ 흐름

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

        public void Restart()
        {
            Time.timeScale = 1f;
            NoiseSystem.Clear();
            GameSession.SkipTitle = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void LoadStage(StageEntry entry)
        {
            if (!StageCatalog.IsSelectable(entry)) return;

            Time.timeScale = 1f;
            NoiseSystem.Clear();
            GameSession.SkipTitle = true;

            if (entry.sceneName == SceneManager.GetActiveScene().name && !MissionStarted)
            {
                // 아직 아무것도 진행되지 않은 새 씬이라면 재로드 없이 바로 시작한다.
                GameSession.SkipTitle = false;
                StartMission();
                return;
            }

            SceneManager.LoadScene(entry.sceneName);
        }

        // ------------------------------------------------------------ 미션

        public void CollectObjective(Objective objective)
        {
            if (State != MissionState.Playing) return;

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
            if (State != MissionState.Playing || CaptureSequenceActive) return;

            CaptureCount++;

            if (CaptureCount >= maxCaptures)
            {
                State = MissionState.Failed;
                Time.timeScale = 0f;
                OnMissionEnded?.Invoke(State);
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
        void TickCaptureSequence()
        {
            captureTimer += Time.unscaledDeltaTime;

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

            float afterFadeOut = captureTimer - fadeOutSeconds;
            if (afterFadeOut < holdBlackSeconds) return;

            float fadeIn = afterFadeOut - holdBlackSeconds;
            if (fadeIn < fadeInSeconds)
            {
                CaptureFade = 1f - fadeIn / Mathf.Max(0.01f, fadeInSeconds);
                return;
            }

            CaptureFade = 0f;
            CaptureSequenceActive = false;
            Time.timeScale = 1f;
        }

        void RespawnPlayer()
        {
            RespawnNotice = respawnNoticeSeconds;

            if (player != null) player.Teleport(playerSpawnPosition, playerSpawnRotation);

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

        public void CompleteMission()
        {
            if (State != MissionState.Playing) return;

            State = MissionState.Success;
            Time.timeScale = 0f;

            StageProgress.MarkCleared(SceneManager.GetActiveScene().name, EvaluateRank());
            OnMissionEnded?.Invoke(State);
        }

        /// <summary>
        /// S / A / B / C 랭크. 얼마나 들키지 않았는가만 본다.
        /// 잠입 게임에서 시간을 걸면 신중하게 플레이한 쪽이 손해를 보므로 시간은 보지 않는다.
        /// </summary>
        public string EvaluateRank()
        {
            if (CaptureCount >= 2) return "C";
            if (CaptureCount == 1) return "B";
            return DetectionCount == 0 ? "S" : "A";
        }
    }
}
