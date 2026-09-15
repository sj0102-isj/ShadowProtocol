using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 탈출 지점. 자료를 모두 회수한 뒤 이 구역에 잠시 머물면 미션 성공.
    /// </summary>
    public class ExitZone : MonoBehaviour
    {
        // 싱글턴(singleton) 패턴 — "씬에 하나만 있는 것"을 어디서든 쉽게 찾게 하는 방법.
        //   ExitZone.Instance 로 바로 접근할 수 있다.
        // 탈출구는 스테이지마다 하나뿐이므로 리스트가 아니라 이 방식이 맞다.
        public static ExitZone Instance { get; private set; }

        [Header("Exit")]
        public float radius = 2.2f;          // 탈출 구역의 반경(m)
        public float extractSeconds = 1.5f;  // 안에 머물러야 하는 시간(초)

        [Header("Visual")]
        public Renderer zoneRenderer;
        public Color lockedColor = new Color(1f, 0.35f, 0.35f, 0.28f);  // 빨강 — 아직 자료가 남음
        public Color readyColor = new Color(0.35f, 1f, 0.6f, 0.35f);    // 초록 — 탈출 가능

        /// <summary>플레이어가 구역 안에 있는가.</summary>
        public bool PlayerInside { get; private set; }

        /// <summary>자료를 다 모아서 탈출이 열렸는가.</summary>
        public bool Unlocked { get; private set; }

        /// <summary>탈출 진행도 0~1. HUD 가 원형 게이지로 보여준다.</summary>
        public float ExtractProgress { get; private set; }

        PlayerController player;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            Instance = this;   // 싱글턴 등록
            mpb = new MaterialPropertyBlock();
            if (zoneRenderer == null) zoneRenderer = GetComponentInChildren<Renderer>();
        }

        void OnDestroy()
        {
            // 나 자신이 등록돼 있을 때만 지운다.
            // 씬을 다시 로드할 때 새 ExitZone 의 Awake 가 먼저 돌고 옛 것의 OnDestroy 가 나중에
            // 도는 경우가 있는데, 이 검사가 없으면 새로 등록된 Instance 를 지워버린다.
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;
            if (player == null || gm == null || gm.State != MissionState.Playing) return;

            Unlocked = gm.AllObjectivesCollected;

            // y 를 0으로 맞춘 좌표끼리 거리를 재면 높이를 무시한 평면 거리가 된다.
            float distance = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));

            PlayerInside = distance <= radius;

            if (PlayerInside && Unlocked)
            {
                // deltaTime 을 '필요한 시간'으로 나누면 1초에 (1/필요시간)씩 차오른다.
                // 즉 extractSeconds 초가 지나면 정확히 1이 된다.
                // Mathf.Max(0.01f, ...) 는 0으로 나누는 사고를 막는 안전장치다.
                ExtractProgress += Time.deltaTime / Mathf.Max(0.01f, extractSeconds);
                if (ExtractProgress >= 1f) gm.CompleteMission();
            }
            else
            {
                // 나가면 진행도가 줄어든다. 즉시 0으로 만들지 않고 서서히 빼서
                // 잠깐 삐끗해도 처음부터 다시 하지 않아도 되게 한다.
                ExtractProgress = Mathf.MoveTowards(ExtractProgress, 0f, Time.deltaTime * 1.5f);
            }

            UpdateVisual();
        }

        /// <summary>구역 바닥 색을 잠김/열림에 따라 바꾼다.</summary>
        void UpdateVisual()
        {
            if (zoneRenderer == null) return;

            Color target = Unlocked ? readyColor : lockedColor;

            // 열렸을 때만 맥동시켜 "여기로 오라"고 알린다.
            if (Unlocked) target.a *= 0.7f + Mathf.PingPong(Time.time * 0.5f, 0.3f);

            zoneRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, target);
            zoneRenderer.SetPropertyBlock(mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 1f, 0.6f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
