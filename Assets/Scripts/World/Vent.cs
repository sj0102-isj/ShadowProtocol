using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 환풍구. 짝이 되는 환풍구로 곧바로 이동한다. 감시망을 통째로 건너뛰는 지름길이라
    /// 경비 배치를 뚫는 대신 '돌아가는 길'을 고르는 선택지가 된다.
    ///
    /// 도착한 직후에는 잠깐 잠겨서, 한 번 누른 것이 왕복으로 이어지지 않는다.
    /// </summary>
    public class Vent : MonoBehaviour
    {
        public static readonly List<Vent> All = new List<Vent>();

        [Header("Link")]
        [Tooltip("이 환풍구와 이어진 반대쪽. 양쪽 모두 서로를 가리켜야 한다.")]
        // 서로를 가리키게 하는 연결은 StageBuilder 가 격자의 같은 숫자(1~9) 두 칸을 찾아 맺어준다.
        public Vent linked;

        [Header("Interaction")]
        public float interactRange = 1.6f;
        [Tooltip("도착한 뒤 다시 쓸 수 있을 때까지의 시간(초).")]
        public float lockoutSeconds = 0.8f;
        public string displayName = "환풍구";

        [Header("Visual")]
        public Renderer grateRenderer;
        public Color idleColor = new Color(0.45f, 0.55f, 0.68f);   // 회색 — 평상시
        public Color readyColor = new Color(0.45f, 0.9f, 1f);      // 하늘색 — 쓸 수 있을 때

        /// <summary>플레이어가 사거리 안에 있고 지금 쓸 수 있는가. HUD 안내에 쓴다.</summary>
        public bool PlayerInRange { get; private set; }

        /// <summary>방금 도착해서 잠겨 있는가.</summary>
        public bool Locked => lockout > 0f;

        PlayerController player;
        MaterialPropertyBlock mpb;
        float lockout;   // 남은 잠금 시간
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (grateRenderer == null) grateRenderer = GetComponentInChildren<Renderer>();
        }

        void Start() => player = Object.FindFirstObjectByType<PlayerController>();

        void Update()
        {
            var gm = StealthGameManager.Instance;

            // gm 이 없어도(테스트 씬 등) 동작하도록 || 로 기본값 true 를 준다.
            bool playing = gm == null || (gm.State == MissionState.Playing && !gm.CaptureSequenceActive);

            if (lockout > 0f) lockout -= Time.deltaTime;

            // 쓸 수 있는 조건을 한 줄로 모았다. 하나라도 어긋나면 false.
            //   게임 중이고 / 잠기지 않았고 / 짝이 있고 / 플레이어가 있고 / 사거리 안
            PlayerInRange = playing && !Locked && linked != null
                            && player != null && InRange(player.transform.position);

            if (PlayerInRange && Pressed()) Travel();

            UpdateVisual();
        }

        /// <summary>V를 이번 프레임에 새로 눌렀는가.</summary>
        static bool Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.vKey.wasPressedThisFrame;
        }

        /// <summary>짝 환풍구로 순간이동시킨다.</summary>
        void Travel()
        {
            // 도착점이 짝 환풍구 바로 위다. 서로를 바라보는 방향은 없으므로 회전은 유지한다.
            player.Teleport(linked.transform.position, player.transform.rotation);

            // 양쪽 모두 잠근다. 한쪽만 잠그면 도착하자마자 V가 다시 먹혀서 왕복한다.
            lockout = lockoutSeconds;
            linked.lockout = linked.lockoutSeconds;
        }

        /// <summary>그 위치가 사거리 안인가. 높이는 무시하고 바닥 평면 거리만 본다.</summary>
        public bool InRange(Vector3 worldPosition)
        {
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        /// <summary>쓸 수 있을 때 하늘색으로 깜박이게 한다.</summary>
        void UpdateVisual()
        {
            if (grateRenderer == null) return;

            Color color = PlayerInRange ? readyColor : idleColor;

            // Color 에 숫자를 곱하면 밝기가 바뀐다. PingPong 으로 0.8~1.2 사이를 왕복시켜 깜박이게.
            if (PlayerInRange) color *= 0.8f + Mathf.PingPong(Time.time * 0.8f, 0.4f);

            grateRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            grateRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>씬 뷰에서 선택했을 때 사거리와 짝까지의 연결선을 그린다.</summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
            if (linked != null) Gizmos.DrawLine(transform.position, linked.transform.position);
        }
    }
}
