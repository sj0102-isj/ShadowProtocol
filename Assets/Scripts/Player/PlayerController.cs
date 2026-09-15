using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 쿼터뷰 잠입 플레이어.
    ///
    /// 조작이 역할별로 갈린다.
    ///   C     : 낮게 기기 — 크롤 통로를 지나기 위한 이동 수단
    ///   H     : 오크통 더미에 숨기 / 나오기
    ///   E     : 기밀 자료 회수(길게)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Speed")]
        [Tooltip("경비 추격 속도(GuardAI.chaseSpeed)보다 빨라야 도망칠 수 있다.")]
        public float walkSpeed = 5.0f;
        [Tooltip("기어갈 때의 속도. 느려야 크롤 통로를 지나는 선택에 무게가 생긴다.")]
        public float crawlSpeed = 2.2f;
        public float acceleration = 22f;
        public float turnSpeedDeg = 900f;

        [Header("Noise")]
        [Tooltip("걸을 때 발소리가 퍼지는 반경(m). 0이면 무소음.")]
        public float walkNoiseRadius = 0f;
        public float noiseInterval = 0.45f;

        [Header("Body")]
        public Transform body;
        public Transform facingMarker;
        public float standHeight = 1.8f;
        [Tooltip("기어갈 때 CharacterController 높이. 크롤 통로 천장(1.25m)보다 낮아야 한다.")]
        public float crawlHeight = 0.95f;

        /// <summary>C를 눌러 낮게 긴 상태. 크롤 통로를 지날 수 있다.</summary>
        public bool IsCrawling { get; private set; }
        public bool IsMoving { get; private set; }

        /// <summary>지금 들어가 있는 은신처. null 이면 밖에 있다.</summary>
        public HideSpot CurrentHideSpot { get; private set; }

        /// <summary>은신처 안에 있어 경비 시야에서 완전히 제외된 상태.</summary>
        public bool IsHidden => CurrentHideSpot != null;

        /// <summary>H로 들어갈 수 있는 은신처가 근처에 있는가. HUD 안내에 쓴다.</summary>
        public HideSpot NearbyHideSpot { get; private set; }

        /// <summary>
        /// 크롤 통로 상인방처럼 머리 위가 막힌 자리에 기어 들어가 있는 상태.
        /// 경비 시야 판정은 눈높이(1.65)에서 플레이어 눈(기어갈 때 0.45)까지 선을 긋는데,
        /// 통로 바로 옆에서는 그 선이 상인방 아래를 스쳐 통로 안이 그대로 들여다보인다.
        /// 그래서 위쪽을 따로 검사해 가려준다.
        /// </summary>
        public bool IsUnderCover { get; private set; }

        /// <summary>
        /// 경비가 나를 얼마나 빨리 알아채는가에 곱해지는 값.
        /// 지금은 이동 방식에 따른 차이가 없어 항상 1이다.
        /// 조명·위장 같은 요소를 붙일 때 여기에 걸면 된다.
        /// </summary>
        public float VisibilityMultiplier => 1f;

        /// <summary>경비가 조준하는 몸통 중심 좌표.</summary>
        public Vector3 EyePoint => transform.position + Vector3.up * (IsCrawling ? 0.45f : 1.2f);

        static readonly RaycastHit[] coverBuffer = new RaycastHit[8];

        CharacterController cc;
        IsoCameraFollow isoCam;
        Vector3 currentVelocity;
        float noiseTimer;
        float verticalVelocity;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            isoCam = Object.FindFirstObjectByType<IsoCameraFollow>();
            if (body == null && transform.childCount > 0) body = transform.GetChild(0);
        }

        void Update()
        {
            var gm = StealthGameManager.Instance;
            if (gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive)) return;

            if (IsHidden)
            {
                TickHidden();
                return;
            }

            TickMoving();
        }

        // ------------------------------------------------------------ 이동

        void TickMoving()
        {
            ReadInput(out Vector2 moveInput, out bool crawlHeld);

            IsCrawling = crawlHeld;
            IsMoving = moveInput.sqrMagnitude > 0.01f;
            UpdateCover();

            Vector3 wish = ScreenToWorldDirection(moveInput);
            float targetSpeed = IsCrawling ? crawlSpeed : walkSpeed;

            currentVelocity = Vector3.MoveTowards(
                currentVelocity, wish * targetSpeed, acceleration * Time.deltaTime);

            if (wish.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, turnSpeedDeg * Time.deltaTime);
            }

            verticalVelocity = cc.isGrounded ? -2f : verticalVelocity + Physics.gravity.y * Time.deltaTime;

            Vector3 motion = currentVelocity;
            motion.y = verticalVelocity;
            cc.Move(motion * Time.deltaTime);

            UpdateBodyVisual();
            UpdateFootstepNoise();
            UpdateHideSpotInteraction();
        }

        void TickHidden()
        {
            IsMoving = false;
            currentVelocity = Vector3.zero;
            noiseTimer = 0f;

            UpdateBodyVisual();

            if (HidePressed()) ExitHideSpot();
        }

        // ------------------------------------------------------------ 은신처

        void UpdateHideSpotInteraction()
        {
            NearbyHideSpot = HideSpot.Nearest(transform.position);
            if (NearbyHideSpot == null) return;
            if (!HidePressed()) return;

            EnterHideSpot(NearbyHideSpot);
        }

        void EnterHideSpot(HideSpot spot)
        {
            CurrentHideSpot = spot;
            spot.IsOccupied = true;
            IsCrawling = false;

            MoveTo(spot.InsidePoint, transform.rotation);
        }

        void ExitHideSpot()
        {
            HideSpot spot = CurrentHideSpot;
            CurrentHideSpot = null;
            if (spot == null) return;

            spot.IsOccupied = false;

            Vector3 exit = spot.ExitPoint;
            Vector3 facing = exit - spot.InsidePoint;
            facing.y = 0f;
            Quaternion rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing, Vector3.up)
                : transform.rotation;

            MoveTo(exit, rotation);
        }

        // ------------------------------------------------------------ 입력

        static void ReadInput(out Vector2 move, out bool crawl)
        {
            move = Vector2.zero;
            crawl = false;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;

            if (move.sqrMagnitude > 1f) move.Normalize();

            crawl = kb.cKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        }

        static bool HidePressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.hKey.wasPressedThisFrame;
        }

        Vector3 ScreenToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = isoCam != null ? isoCam.FlatForward : Vector3.forward;
            Vector3 right = isoCam != null ? isoCam.FlatRight : Vector3.right;

            Vector3 dir = forward * input.y + right * input.x;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        /// <summary>
        /// 머리 위가 막혀 있는지 본다. 서 있을 때는 애초에 상인방에 막혀 들어갈 수 없으므로
        /// 기어가는 중일 때만 검사한다. 자기 콜라이더는 건너뛴다.
        /// </summary>
        void UpdateCover()
        {
            if (!IsCrawling)
            {
                IsUnderCover = false;
                return;
            }

            // 기어가는 캡슐(높이 0.95) 위, 상인방 바닥(1.25) 아래에서 쏜다.
            Vector3 origin = transform.position + Vector3.up * 1.05f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.up, coverBuffer, 1.2f,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = coverBuffer[i].collider;
                if (col == null || col.GetComponentInParent<PlayerController>() == this) continue;

                IsUnderCover = true;
                return;
            }

            IsUnderCover = false;
        }

        // ------------------------------------------------------------ 표현

        void UpdateBodyVisual()
        {
            // 숨어 있을 때도 기어갈 때와 같은 낮은 실루엣을 쓴다.
            bool low = IsCrawling || IsHidden;

            float targetHeight = low ? crawlHeight : standHeight;
            cc.height = Mathf.Lerp(cc.height, targetHeight, 12f * Time.deltaTime);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);

            if (body != null)
            {
                Vector3 s = body.localScale;
                s.y = Mathf.Lerp(s.y, low ? 0.32f : 0.9f, 12f * Time.deltaTime);
                body.localScale = s;
                body.localPosition = new Vector3(0f, s.y, 0f);
            }

            if (facingMarker != null)
            {
                Vector3 p = facingMarker.localPosition;
                p.y = Mathf.Lerp(p.y, low ? 0.42f : 1.25f, 12f * Time.deltaTime);
                facingMarker.localPosition = p;
                facingMarker.gameObject.SetActive(!IsHidden);
            }
        }

        void UpdateFootstepNoise()
        {
            float radius = IsCrawling ? 0f : walkNoiseRadius;
            if (!IsMoving || radius <= 0f)
            {
                noiseTimer = 0f;
                return;
            }

            noiseTimer -= Time.deltaTime;
            if (noiseTimer > 0f) return;

            noiseTimer = noiseInterval;
            NoiseSystem.Emit(transform.position, radius, 0.7f);
        }

        // ------------------------------------------------------------ 위치

        /// <summary>
        /// CharacterController는 transform 위치를 자기가 덮어쓰므로 순간이동 시 잠시 꺼야 한다.
        /// </summary>
        void MoveTo(Vector3 position, Quaternion rotation)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            cc.enabled = true;

            currentVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        /// <summary>붙잡혀서 시작 지점으로 되돌릴 때 사용.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (CurrentHideSpot != null)
            {
                CurrentHideSpot.IsOccupied = false;
                CurrentHideSpot = null;
            }

            NearbyHideSpot = null;
            IsCrawling = false;
            noiseTimer = 0f;

            MoveTo(position, rotation);
        }
    }
}
