using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 동전 던지기. 마우스 커서가 가리키는 바닥으로 던진다.
    /// 착지 소리를 들은 경비가 그쪽으로 확인하러 가므로, 순찰 구멍을 원하는 자리에 만들 수 있다.
    /// </summary>
    //
    // 역할 분담:
    //   CoinThrow (이 파일) — 어디를 조준하는가, 던져도 되는가, 언제 던지는가
    //   Coin                — 날아가고, 튕기고, 멈추고, 소리를 낸다
    // 조준은 플레이어의 일이고 비행은 동전의 일이라서 이렇게 나눴다.
    public class CoinThrow : MonoBehaviour
    {
        [Header("Throw")]
        [Tooltip("던질 수 있는 최대 거리(m). 너무 멀리 보내면 경비가 자리를 오래 비워 난이도가 무너진다.")]
        public float maxRange = 12f;
        public float cooldown = 12f;

        [Header("Refs")]
        public Coin coin;               // 재사용하는 동전 하나
        [Tooltip("커서가 가리키는 착지 지점 표시. 던질 수 있을 때만 보인다.")]
        public Transform aimMarker;
        public Renderer aimRenderer;

        [Header("Aim Colors")]
        public Color validColor = new Color(1f, 0.85f, 0.3f, 0.45f);    // 노랑 — 던질 수 있는 자리
        public Color invalidColor = new Color(1f, 0.35f, 0.35f, 0.30f); // 빨강 — 바닥이 아님

        /// <summary>다시 던질 수 있을 때까지 남은 시간(초). HUD 가 표시한다.</summary>
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
        bool aimValid;       // 지금 조준점이 실제로 던질 수 있는 자리인가
        Vector3 aimPoint;    // 조준점의 월드 좌표

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Plane = 무한히 넓은 평면. (법선 = 위쪽, 원점을 지남) 이면 곧 y=0 인 바닥면이다.
        // 실제 바닥 콜라이더에 레이를 쏘는 것보다 계산이 훨씬 싸고, 바닥이 없는 곳에서도 동작한다.
        static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        /// <summary>던지는 손의 높이. 크롤 통로 상인방(1.25) 아래라 낮은 통로로도 굴려 보낼 수 있다.</summary>
        // const = 절대 변하지 않는 값. 인스펙터에도 안 나오고 실행 중에도 못 바꾼다.
        // 게임 규칙상 바뀌면 안 되는 값은 public 필드보다 const 가 안전하다.
        const float ThrowHeight = 1.1f;

        /// <summary>플레이어 캡슐 밖에서 출발시킨다. 안에서 놓으면 자기 몸에 튕겨 엉뚱하게 날아간다.</summary>
        const float ThrowOffset = 0.7f;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            mpb = new MaterialPropertyBlock();
            if (aimMarker != null) aimMarker.gameObject.SetActive(false);
        }

        // Camera.main 은 "MainCamera" 태그가 붙은 카메라를 찾아준다.
        // 찾는 비용이 있어서 매 프레임 부르지 않고 한 번만 담아둔다.
        void Start() => cam = Camera.main;

        void Update()
        {
            var gm = StealthGameManager.Instance;
            bool playing = gm == null || (gm.State == MissionState.Playing && !gm.CaptureSequenceActive);

            if (CooldownRemaining > 0f) CooldownRemaining -= Time.deltaTime;

            // 숨어 있는 동안에는 던질 수 없다. 통 안에서 동전이 날아가면 이상하다.
            if (!playing || (player != null && player.IsHidden))
            {
                ShowAim(false);
                return;
            }

            UpdateAim();

            var kb = Keyboard.current;
            if (kb != null && kb.qKey.wasPressedThisFrame && Ready && aimValid) Fire();
        }

        /// <summary>마우스 커서 아래의 바닥 좌표를 구하고 조준 표시를 갱신한다.</summary>
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
            // ScreenPointToRay = 화면 좌표(픽셀)에서 월드로 쏘는 광선을 만든다.
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            // Plane.Raycast 는 "광선이 이 평면과 만나는가"를 보고, 만나면 거리를 준다.
            if (!GroundPlane.Raycast(ray, out float distance))
            {
                ShowAim(false);
                return;
            }

            Vector3 point = ray.GetPoint(distance);   // 광선 위 distance 만큼 간 지점

            // 사거리 밖이면 방향은 유지하고 거리만 자른다.
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            if (offset.magnitude > maxRange) point = transform.position + offset.normalized * maxRange;

            // 실제로 바닥인 자리여야 한다. 벽 안이나 맵 밖은 안 된다.
            // NavMesh 를 "걸을 수 있는 바닥인가"의 판정 기준으로 재활용한 것이다.
            aimValid = NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas);
            aimPoint = aimValid ? hit.position : point;

            ShowAim(true);
        }

        /// <summary>바닥 조준 표시를 켜고 끄고 색을 맞춘다.</summary>
        void ShowAim(bool visible)
        {
            if (aimMarker == null) return;

            // 쿨다운 중이면 보여줘봐야 던질 수 없으므로 아예 숨긴다.
            bool show = visible && Ready;
            aimMarker.gameObject.SetActive(show);
            if (!show) return;

            aimMarker.position = aimPoint + Vector3.up * 0.04f;   // 바닥보다 살짝 띄운다

            if (aimRenderer == null) return;
            aimRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, aimValid ? validColor : invalidColor);
            aimRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>실제로 던진다.</summary>
        void Fire()
        {
            Vector3 hand = transform.position + Vector3.up * ThrowHeight;

            // 조준 방향으로 조금 앞에서 출발시킨다.
            Vector3 forward = aimPoint - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f) hand += forward.normalized * ThrowOffset;

            coin.Throw(hand, aimPoint);
            CooldownRemaining = cooldown;
            ShowAim(false);
        }
    }
}
