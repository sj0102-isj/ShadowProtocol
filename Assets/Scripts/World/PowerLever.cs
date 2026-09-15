using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 두꺼비집 레버. F를 눌러 내리면 정전이 시작된다.
    /// 다시 쓰려면 재충전을 기다려야 해서, 아무 때나 누르는 안전 버튼이 되지 않는다.
    /// </summary>
    public class PowerLever : MonoBehaviour
    {
        public static readonly List<PowerLever> All = new List<PowerLever>();

        [Header("Interaction")]
        public float interactRange = 1.9f;
        [Tooltip("한 번 쓰고 다시 쓸 수 있을 때까지의 시간(초).")]
        public float cooldown = 20f;
        public string displayName = "두꺼비집";

        [Header("Visual")]
        [Tooltip("내렸을 때 회전하는 손잡이.")]
        public Transform handle;
        public Renderer statusRenderer;
        public Color readyColor = new Color(0.35f, 1f, 0.6f);
        public Color chargingColor = new Color(0.55f, 0.57f, 0.62f);
        public Color activeColor = new Color(1f, 0.85f, 0.3f);

        public bool PlayerInRange { get; private set; }
        public float CooldownRemaining { get; private set; }
        public bool Ready => CooldownRemaining <= 0f;

        PlayerController player;
        MaterialPropertyBlock mpb;
        float handleAngle;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (statusRenderer == null) statusRenderer = GetComponentInChildren<Renderer>();
        }

        void Start() => player = Object.FindFirstObjectByType<PlayerController>();

        void Update()
        {
            var gm = StealthGameManager.Instance;
            bool playing = gm == null || (gm.State == MissionState.Playing && !gm.CaptureSequenceActive);

            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;

            PlayerInRange = playing && player != null && InRange(player.transform.position);

            if (PlayerInRange && Ready && Pressed()) Pull();

            UpdateVisual();
        }

        static bool Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.fKey.wasPressedThisFrame;
        }

        void Pull()
        {
            if (BlackoutSystem.Instance == null)
            {
                Debug.LogWarning("[PowerLever] 씬에 BlackoutSystem 이 없습니다.");
                return;
            }

            BlackoutSystem.Instance.Trigger();
            CooldownRemaining = cooldown;
        }

        public bool InRange(Vector3 worldPosition)
        {
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        void UpdateVisual()
        {
            bool blackout = BlackoutSystem.PlayerIsInvisible;

            if (handle != null)
            {
                // 내려간 상태를 각도로 보여준다. 재충전이 끝나면 원래대로 올라간다.
                float target = Ready ? -35f : 35f;
                handleAngle = Mathf.MoveTowards(handleAngle, target, 180f * Time.deltaTime);
                handle.localRotation = Quaternion.Euler(handleAngle, 0f, 0f);
            }

            if (statusRenderer == null) return;

            Color color = blackout ? activeColor : (Ready ? readyColor : chargingColor);
            if (Ready && !blackout) color *= 0.75f + Mathf.PingPong(Time.time * 0.6f, 0.3f);

            statusRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            statusRenderer.SetPropertyBlock(mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
