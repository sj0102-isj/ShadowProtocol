using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 탈출 지점. 자료를 모두 회수한 뒤 이 구역에 잠시 머물면 미션 성공.
    /// </summary>
    public class ExitZone : MonoBehaviour
    {
        public static ExitZone Instance { get; private set; }

        [Header("Exit")]
        public float radius = 2.2f;
        public float extractSeconds = 1.5f;

        [Header("Visual")]
        public Renderer zoneRenderer;
        public Color lockedColor = new Color(1f, 0.35f, 0.35f, 0.28f);
        public Color readyColor = new Color(0.35f, 1f, 0.6f, 0.35f);

        public bool PlayerInside { get; private set; }
        public bool Unlocked { get; private set; }
        public float ExtractProgress { get; private set; }

        PlayerController player;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            Instance = this;
            mpb = new MaterialPropertyBlock();
            if (zoneRenderer == null) zoneRenderer = GetComponentInChildren<Renderer>();
        }

        void OnDestroy()
        {
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

            float distance = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));

            PlayerInside = distance <= radius;

            if (PlayerInside && Unlocked)
            {
                ExtractProgress += Time.deltaTime / Mathf.Max(0.01f, extractSeconds);
                if (ExtractProgress >= 1f) gm.CompleteMission();
            }
            else
            {
                ExtractProgress = Mathf.MoveTowards(ExtractProgress, 0f, Time.deltaTime * 1.5f);
            }

            UpdateVisual();
        }

        void UpdateVisual()
        {
            if (zoneRenderer == null) return;

            Color target = Unlocked ? readyColor : lockedColor;
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
