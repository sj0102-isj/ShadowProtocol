using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 두꺼비집 레버. F를 눌러 내리면 정전이 시작된다.
    /// 다시 쓰려면 재충전을 기다려야 해서, 아무 때나 누르는 안전 버튼이 되지 않는다.
    /// </summary>
    //
    // 이 스크립트는 "레버를 당길 수 있는가"만 판단하고, 정전 자체는 BlackoutSystem 이 한다.
    // 역할을 쪼개 두면 나중에 다른 방법으로 정전을 일으키고 싶을 때(타이머, 아이템 등)
    // BlackoutSystem.Trigger() 만 부르면 되고 이 파일은 손대지 않아도 된다.
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
        public Renderer statusRenderer;                                  // 상태 표시등
        public Color readyColor = new Color(0.35f, 1f, 0.6f);            // 초록 — 쓸 수 있음
        public Color chargingColor = new Color(0.55f, 0.57f, 0.62f);     // 회색 — 재충전 중
        public Color activeColor = new Color(1f, 0.85f, 0.3f);           // 노랑 — 정전 중

        /// <summary>플레이어가 사거리 안에 있는가. HUD 안내에 쓴다.</summary>
        public bool PlayerInRange { get; private set; }

        /// <summary>재충전까지 남은 시간(초). HUD 가 표시한다.</summary>
        public float CooldownRemaining { get; private set; }

        /// <summary>지금 당길 수 있는가.</summary>
        public bool Ready => CooldownRemaining <= 0f;

        PlayerController player;
        MaterialPropertyBlock mpb;
        float handleAngle;   // 손잡이의 현재 각도. 목표로 서서히 움직인다
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

            // 재충전 타이머는 게임이 진행 중이 아니어도 흐른다. 메뉴에 있는 동안 충전되는 것이
            // 이상하지만, 메뉴에서는 어차피 Time.timeScale 이 0이라 deltaTime 도 0이다.
            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;

            PlayerInRange = playing && player != null && InRange(player.transform.position);

            if (PlayerInRange && Ready && Pressed()) Pull();

            UpdateVisual();
        }

        /// <summary>F를 이번 프레임에 새로 눌렀는가.</summary>
        static bool Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.fKey.wasPressedThisFrame;
        }

        /// <summary>레버를 내린다. 실제 정전 처리는 BlackoutSystem 이 한다.</summary>
        void Pull()
        {
            if (BlackoutSystem.Instance == null)
            {
                // Debug.LogWarning 은 콘솔에 노란 경고를 찍는다. 게임을 멈추지는 않는다.
                // 씬 구성이 잘못됐을 때 원인을 바로 알 수 있어서 이런 방어 코드는 값어치가 있다.
                Debug.LogWarning("[PowerLever] 씬에 BlackoutSystem 이 없습니다.");
                return;
            }

            BlackoutSystem.Instance.Trigger();
            CooldownRemaining = cooldown;
        }

        /// <summary>그 위치가 사거리 안인가. 높이는 무시하고 바닥 평면 거리만 본다.</summary>
        public bool InRange(Vector3 worldPosition)
        {
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        /// <summary>손잡이 각도와 표시등 색을 상태에 맞춰 갱신한다.</summary>
        void UpdateVisual()
        {
            bool blackout = BlackoutSystem.PlayerIsInvisible;

            if (handle != null)
            {
                // 내려간 상태를 각도로 보여준다. 재충전이 끝나면 원래대로 올라간다.
                float target = Ready ? -35f : 35f;

                // MoveTowards 는 초당 180도씩 목표로 다가간다. 즉시 바뀌지 않아 레버가 움직여 보인다.
                handleAngle = Mathf.MoveTowards(handleAngle, target, 180f * Time.deltaTime);
                handle.localRotation = Quaternion.Euler(handleAngle, 0f, 0f);
            }

            if (statusRenderer == null) return;

            // 괄호 안의 삼항 연산자가 중첩됐다. 읽는 순서는:
            //   정전 중이면 노랑, 아니면 (쓸 수 있으면 초록, 아니면 회색)
            Color color = blackout ? activeColor : (Ready ? readyColor : chargingColor);

            // 쓸 수 있을 때만 깜박여서 눈에 띄게 한다.
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
