using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 훔쳐야 하는 자료. 범위 안에서 E를 길게 눌러 회수한다.
    /// 즉시 획득이 아니라 홀드로 만든 이유: 회수 중에 경비가 다가오는 긴장감이 생기고,
    /// 영상에서도 진행 바가 차는 그림이 나온다.
    /// </summary>
    public class Objective : MonoBehaviour
    {
        public static readonly List<Objective> All = new List<Objective>();

        [Header("Interaction")]
        public float interactRange = 1.8f;
        public float holdDuration = 1.2f;
        public string displayName = "기밀 자료";

        [Header("Value")]
        [Tooltip("회수했을 때 오르는 점수. 무한모드의 중앙 자료처럼 위험한 자리일수록 높게 둔다.")]
        public int points = 1;
        [Tooltip("무한모드에서 회수 후 무작위 위치가 아니라 원래 자리에서 되살아난다.")]
        public bool fixedSpawn;
        [Tooltip(
            "회수 후 다시 나타나기까지의 시간(초). 0이면 즉시 되살아난다.\n" +
            "고액 자료에 쿨다운을 두는 이유는, 없으면 위험한 한가운데만 반복해서 도는 것이 " +
            "최적해가 되어 맵의 나머지가 놀아버리기 때문이다.")]
        public float respawnDelay;

        [Header("Visual")]
        public Transform visual;
        public float spinSpeed = 60f;
        public float bobHeight = 0.15f;
        public float bobSpeed = 2f;

        public bool Collected { get; private set; }
        public bool PlayerInRange { get; private set; }
        public float HoldProgress { get; private set; }

        /// <summary>고정 자료가 되살아날 원래 자리.</summary>
        public Vector3 HomePosition { get; private set; }

        PlayerController player;
        Vector3 visualBasePos;
        float respawnTimer;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake() => HomePosition = transform.position;

        void Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) visualBasePos = visual.localPosition;
        }

        /// <summary>쿨다운이 있는 자료가 다시 나타나기까지 남은 시간. 0이면 대기 중이 아니다.</summary>
        public float RespawnRemaining => Collected ? Mathf.Max(0f, respawnTimer) : 0f;

        void Update()
        {
            if (Collected)
            {
                TickRespawn();
                return;
            }

            AnimateVisual();

            if (player == null ||
                (StealthGameManager.Instance != null &&
                 StealthGameManager.Instance.State != MissionState.Playing))
            {
                PlayerInRange = false;
                HoldProgress = 0f;
                return;
            }

            float distance = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));

            PlayerInRange = distance <= interactRange;

            if (!PlayerInRange)
            {
                HoldProgress = Mathf.MoveTowards(HoldProgress, 0f, Time.deltaTime * 2f);
                return;
            }

            bool holding = Keyboard.current != null && Keyboard.current.eKey.isPressed;

            if (holding)
            {
                HoldProgress += Time.deltaTime / Mathf.Max(0.01f, holdDuration);
                if (HoldProgress >= 1f) Collect();
            }
            else
            {
                HoldProgress = Mathf.MoveTowards(HoldProgress, 0f, Time.deltaTime * 2f);
            }
        }

        /// <summary>
        /// 쿨다운이 걸린 자료를 제자리에서 되살린다. 무한모드에서만 동작한다.
        /// 일반 스테이지에서 되살아나면 "자료를 전부 모은다"는 조건 자체가 성립하지 않는다.
        /// </summary>
        void TickRespawn()
        {
            if (respawnDelay <= 0f) return;

            var gm = StealthGameManager.Instance;
            if (gm == null || !gm.endlessMode || gm.State != MissionState.Playing) return;

            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f) RespawnAt(HomePosition);
        }

        void AnimateVisual()
        {
            if (visual == null) return;
            visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
            visual.localPosition = visualBasePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
        }

        /// <summary>
        /// 무한모드용. 회수된 자료를 없애고 새로 만드는 대신 다른 자리로 옮겨 되살린다.
        /// 개수가 항상 일정하게 유지되고 생성·파괴 비용도 없다.
        /// </summary>
        public void RespawnAt(Vector3 position)
        {
            transform.position = position;

            Collected = false;
            HoldProgress = 0f;
            PlayerInRange = false;

            if (visual != null) visual.gameObject.SetActive(true);

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = true;
        }

        void Collect()
        {
            Collected = true;
            HoldProgress = 1f;
            PlayerInRange = false;
            respawnTimer = respawnDelay;

            // 숨기는 처리를 먼저 끝낸다. 무한모드에서는 아래 CollectObjective 안에서
            // 곧바로 RespawnAt 이 불려 다른 자리에서 되살아나는데, 그 뒤에 숨기면
            // 되살아난 자료를 도로 꺼버려서 "재생성이 안 되는" 것처럼 보인다.
            if (visual != null) visual.gameObject.SetActive(false);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            if (StealthGameManager.Instance != null)
                StealthGameManager.Instance.CollectObjective(this);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.9f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
