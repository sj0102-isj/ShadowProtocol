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
        public Vent linked;

        [Header("Interaction")]
        public float interactRange = 1.6f;
        [Tooltip("도착한 뒤 다시 쓸 수 있을 때까지의 시간(초).")]
        public float lockoutSeconds = 0.8f;
        public string displayName = "환풍구";

        [Header("Visual")]
        public Renderer grateRenderer;
        public Color idleColor = new Color(0.45f, 0.55f, 0.68f);
        public Color readyColor = new Color(0.45f, 0.9f, 1f);

        public bool PlayerInRange { get; private set; }
        public bool Locked => lockout > 0f;

        PlayerController player;
        MaterialPropertyBlock mpb;
        float lockout;
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
            bool playing = gm == null || (gm.State == MissionState.Playing && !gm.CaptureSequenceActive);

            if (lockout > 0f) lockout -= Time.deltaTime;

            PlayerInRange = playing && !Locked && linked != null
                            && player != null && InRange(player.transform.position);

            if (PlayerInRange && Pressed()) Travel();

            UpdateVisual();
        }

        static bool Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.vKey.wasPressedThisFrame;
        }

        void Travel()
        {
            // 도착점이 짝 환풍구 바로 위다. 서로를 바라보는 방향은 없으므로 회전은 유지한다.
            player.Teleport(linked.transform.position, player.transform.rotation);

            lockout = lockoutSeconds;
            linked.lockout = linked.lockoutSeconds;
        }

        public bool InRange(Vector3 worldPosition)
        {
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        void UpdateVisual()
        {
            if (grateRenderer == null) return;

            Color color = PlayerInRange ? readyColor : idleColor;
            if (PlayerInRange) color *= 0.8f + Mathf.PingPong(Time.time * 0.8f, 0.4f);

            grateRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            grateRenderer.SetPropertyBlock(mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
            if (linked != null) Gizmos.DrawLine(transform.position, linked.transform.position);
        }
    }
}
