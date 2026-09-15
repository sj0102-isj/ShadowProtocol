using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 동전 던지기. 마우스 커서가 가리키는 바닥으로 던진다.
    /// 착지 소리를 들은 경비가 그쪽으로 확인하러 가므로, 순찰 구멍을 원하는 자리에 만들 수 있다.
    /// </summary>
    public class CoinThrow : MonoBehaviour
    {
        [Header("Throw")]
        [Tooltip("던질 수 있는 최대 거리(m). 너무 멀리 보내면 경비가 자리를 오래 비워 난이도가 무너진다.")]
        public float maxRange = 12f;
        public float cooldown = 12f;

        [Header("Refs")]
        public Coin coin;
        [Tooltip("커서가 가리키는 착지 지점 표시. 던질 수 있을 때만 보인다.")]
        public Transform aimMarker;
        public Renderer aimRenderer;

        [Header("Aim Colors")]
        public Color validColor = new Color(1f, 0.85f, 0.3f, 0.45f);
        public Color invalidColor = new Color(1f, 0.35f, 0.35f, 0.30f);

        public float CooldownRemaining { get; private set; }

        /// <summary>
        /// 재사용 쿨다운만 본다. 예전에는 "동전이 회수됐을 것"까지 요구했는데,
        /// 아무도 주우러 오지 않으면 동전이 12초간 바닥에 남아 그동안 조준선조차 뜨지 않았다.
        /// 방이 나뉜 스테이지 2에서는 경비가 안 오는 경우가 많아 아예 고장난 것처럼 보였다.
        /// 이제 다시 던지면 바닥의 동전이 그대로 새 자리로 날아간다.
        /// </summary>
        public bool Ready => CooldownRemaining <= 0f && coin != null;

        Camera cam;
        PlayerController player;
        MaterialPropertyBlock mpb;
        bool aimValid;
        Vector3 aimPoint;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        /// <summary>던지는 손의 높이. 크롤 통로 상인방(1.25) 아래라 낮은 통로로도 굴려 보낼 수 있다.</summary>
        const float ThrowHeight = 1.1f;

        /// <summary>플레이어 캡슐 밖에서 출발시킨다. 안에서 놓으면 자기 몸에 튕겨 엉뚱하게 날아간다.</summary>
        const float ThrowOffset = 0.7f;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            mpb = new MaterialPropertyBlock();
            if (aimMarker != null) aimMarker.gameObject.SetActive(false);
        }

        void Start() => cam = Camera.main;

        void Update()
        {
            var gm = StealthGameManager.Instance;
            bool playing = gm == null || (gm.State == MissionState.Playing && !gm.CaptureSequenceActive);

            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;

            if (!playing || (player != null && player.IsHidden))
            {
                ShowAim(false);
                return;
            }

            UpdateAim();

            var kb = Keyboard.current;
            if (kb != null && kb.qKey.wasPressedThisFrame && Ready && aimValid) Fire();
        }

        void UpdateAim()
        {
            aimValid = false;

            if (cam == null) cam = Camera.main;
            var mouse = Mouse.current;
            if (cam == null || mouse == null)
            {
                ShowAim(false);
                return;
            }

            // 화면의 커서를 바닥 평면(y=0)에 투영한다.
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!GroundPlane.Raycast(ray, out float distance))
            {
                ShowAim(false);
                return;
            }

            Vector3 point = ray.GetPoint(distance);

            // 사거리 밖이면 방향은 유지하고 거리만 자른다.
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            if (offset.magnitude > maxRange) point = transform.position + offset.normalized * maxRange;

            // 실제로 바닥인 자리여야 한다. 벽 안이나 맵 밖은 안 된다.
            aimValid = NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas);
            aimPoint = aimValid ? hit.position : point;

            ShowAim(true);
        }

        void ShowAim(bool visible)
        {
            if (aimMarker == null) return;

            bool show = visible && Ready;
            aimMarker.gameObject.SetActive(show);
            if (!show) return;

            aimMarker.position = aimPoint + Vector3.up * 0.04f;

            if (aimRenderer == null) return;
            aimRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, aimValid ? validColor : invalidColor);
            aimRenderer.SetPropertyBlock(mpb);
        }

        void Fire()
        {
            Vector3 hand = transform.position + Vector3.up * ThrowHeight;

            Vector3 forward = aimPoint - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f) hand += forward.normalized * ThrowOffset;

            coin.Throw(hand, aimPoint);
            CooldownRemaining = cooldown;
            ShowAim(false);
        }
    }
}
