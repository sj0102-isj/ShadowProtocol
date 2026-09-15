// using = "이 파일에서 쓸 도구 상자를 연다"는 뜻이다.
using UnityEngine;                  // Vector3, Transform, Mathf 같은 유니티 기본 기능
using UnityEngine.InputSystem;      // Keyboard.current — 새 입력 시스템

// namespace = 이름이 겹치지 않게 묶어두는 서랍.
// 이 프로젝트의 스크립트는 전부 Stealth 서랍 안에 있어서, 서로를 그냥 이름으로 부를 수 있다.
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
    // [RequireComponent] = 이 스크립트를 붙인 오브젝트에 CharacterController 가 없으면
    // 유니티가 자동으로 붙여준다. 없어서 터지는 사고를 원천 차단하는 장치.
    [RequireComponent(typeof(CharacterController))]
    // MonoBehaviour 를 상속하면 유니티가 Awake/Update 같은 함수를 알아서 호출해 준다.
    // 게임 오브젝트에 붙일 수 있는 부품(컴포넌트)이 되는 조건이다.
    public class PlayerController : MonoBehaviour
    {
        // ============================================================
        //  인스펙터에 보이는 설정값들
        //
        //  public 필드는 유니티 인스펙터 창에 칸으로 나타나서, 코드를 고치지 않고도
        //  플레이 중에 숫자를 바꿔 볼 수 있다. 밸런스를 잡을 때 이게 제일 빠르다.
        //  [Header] 는 인스펙터에 굵은 제목을 찍고, [Tooltip] 은 마우스를 올리면 뜨는 설명이다.
        // ============================================================

        [Header("Speed")]
        [Tooltip("경비 추격 속도(GuardAI.chaseSpeed)보다 빨라야 도망칠 수 있다.")]
        public float walkSpeed = 5.0f;          // 서서 걸을 때 최고 속도 (초당 미터)
        [Tooltip("기어갈 때의 속도. 느려야 크롤 통로를 지나는 선택에 무게가 생긴다.")]
        public float crawlSpeed = 2.2f;         // 기어갈 때 최고 속도
        public float acceleration = 22f;        // 최고 속도에 도달하는 빠르기. 클수록 즉각적으로 움직인다
        public float turnSpeedDeg = 900f;       // 몸이 도는 속도 (초당 각도). 900이면 거의 즉시 돈다

        [Header("Noise")]
        [Tooltip("걸을 때 발소리가 퍼지는 반경(m). 0이면 무소음.")]
        public float walkNoiseRadius = 0f;      // 0 = 발소리 기능이 꺼져 있다는 뜻
        public float noiseInterval = 0.45f;     // 발소리를 내는 간격(초). 0.45초마다 한 발

        [Header("Body")]
        public Transform body;                  // 눈에 보이는 캡슐 몸통. 웅크리면 납작해진다
        public Transform facingMarker;          // 어디를 보는지 알려주는 코 모양 막대
        public float standHeight = 1.8f;        // 서 있을 때 충돌 캡슐의 높이
        [Tooltip("기어갈 때 CharacterController 높이. 크롤 통로 천장(1.25m)보다 낮아야 한다.")]
        public float crawlHeight = 0.95f;       // 이 값이 1.25보다 크면 크롤 통로에 못 들어간다

        // ============================================================
        //  바깥에서 읽기만 하는 상태값 (프로퍼티)
        //
        //  { get; private set; } = "밖에서는 읽을 수만 있고, 값을 바꾸는 건 이 클래스 안에서만".
        //  경비·HUD·자료 같은 다른 스크립트가 플레이어 상태를 물어볼 때 쓴다.
        // ============================================================

        /// <summary>C를 눌러 낮게 긴 상태. 크롤 통로를 지날 수 있다.</summary>
        public bool IsCrawling { get; private set; }

        /// <summary>지금 움직이고 있는가. 발소리를 낼지 판단할 때 쓴다.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>지금 들어가 있는 은신처. null 이면 밖에 있다.</summary>
        public HideSpot CurrentHideSpot { get; private set; }

        /// <summary>은신처 안에 있어 경비 시야에서 완전히 제외된 상태.</summary>
        // => 는 "계산해서 돌려주는 프로퍼티"다. 값을 따로 저장하지 않고 물어볼 때마다 계산한다.
        // 저장해 두면 CurrentHideSpot 과 어긋날 수 있는데, 이렇게 하면 어긋날 수가 없다.
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
        // transform.position 은 발밑(바닥) 좌표다. 거기서 위로 얼마나 올릴지를 더한다.
        // ?: 는 삼항 연산자 — "조건 ? 참일때값 : 거짓일때값".
        // 기어가면 0.45m, 서 있으면 1.2m 높이를 몸통 중심으로 본다.
        public Vector3 EyePoint => transform.position + Vector3.up * (IsCrawling ? 0.45f : 1.2f);

        // ============================================================
        //  내부에서만 쓰는 변수들 (private — 접근 지정자를 안 쓰면 기본이 private)
        // ============================================================

        // static = 이 클래스 전체가 하나만 공유하는 값. 플레이어가 몇 명이든 배열은 하나다.
        // 레이캐스트 결과를 담을 그릇을 미리 만들어 두고 계속 재사용한다.
        // 매 프레임 새 배열을 만들면 쓰레기가 쌓여 가비지 컬렉션이 돌고, 그때 화면이 튄다.
        static readonly RaycastHit[] coverBuffer = new RaycastHit[8];

        CharacterController cc;         // 유니티가 주는 이동·충돌 부품. 벽을 통과하지 않게 해준다
        IsoCameraFollow isoCam;         // 쿼터뷰 카메라. 입력 방향을 화면 기준으로 바꿀 때 필요
        Vector3 currentVelocity;        // 지금 실제 속도(x,z). 목표 속도로 서서히 다가간다
        float noiseTimer;               // 다음 발소리까지 남은 시간
        float verticalVelocity;         // 중력으로 인한 아래 방향 속도

        // ============================================================
        //  유니티가 자동으로 부르는 함수들
        // ============================================================

        /// <summary>
        /// Awake — 오브젝트가 만들어질 때 딱 한 번. 부품을 찾아 변수에 담아두는 자리.
        /// 매 프레임 GetComponent 를 부르면 느리므로 여기서 한 번만 찾는다.
        /// </summary>
        void Awake()
        {
            cc = GetComponent<CharacterController>();               // 나 자신에게 붙은 부품 찾기
            isoCam = Object.FindFirstObjectByType<IsoCameraFollow>(); // 씬 전체에서 카메라 찾기

            // body 를 인스펙터에서 안 넣어줬으면 첫 번째 자식을 몸통으로 삼는다.
            // 이런 보호 코드가 있으면 씬을 새로 만들 때 연결을 빼먹어도 돌아간다.
            if (body == null && transform.childCount > 0) body = transform.GetChild(0);
        }

        /// <summary>
        /// Update — 매 프레임 호출된다. 초당 60번쯤.
        /// 여기서 하는 일은 "지금 어떤 상태인지 보고 알맞은 함수로 넘기기" 뿐이다.
        /// </summary>
        void Update()
        {
            // var = 타입을 컴파일러가 알아서 추론하게 하는 키워드. 여기서는 StealthGameManager.
            var gm = StealthGameManager.Instance;

            // 메뉴 화면이거나, 붙잡혀서 연출이 재생 중이면 조작을 받지 않는다.
            // return 은 "이 함수를 여기서 끝낸다"는 뜻이라 아래 코드가 실행되지 않는다.
            if (gm != null && (gm.State != MissionState.Playing || gm.CaptureSequenceActive)) return;

            // 상태 기계(state machine). 숨어 있을 때와 아닐 때 하는 일이 완전히 다르다.
            if (IsHidden)
            {
                TickHidden();
                return;
            }

            TickMoving();
        }

        // ------------------------------------------------------------ 이동

        /// <summary>밖에 나와 있을 때의 한 프레임. 입력 → 이동 → 표현 순서로 처리한다.</summary>
        void TickMoving()
        {
            // out 매개변수 = 함수가 값을 두 개 이상 돌려줄 때 쓰는 방식.
            // ReadInput 이 moveInput 과 crawlHeld 두 개를 채워서 돌려준다.
            ReadInput(out Vector2 moveInput, out bool crawlHeld);

            IsCrawling = crawlHeld;

            // sqrMagnitude = 길이의 제곱. 진짜 길이(magnitude)는 제곱근 계산이 들어가서 느리다.
            // "길이가 0.1보다 큰가"를 묻고 싶으면 "제곱이 0.01보다 큰가"로 물으면 같은 답이 나온다.
            IsMoving = moveInput.sqrMagnitude > 0.01f;

            UpdateCover();

            // 화면 기준 입력(위/아래/왼/오른)을 월드 기준 방향으로 바꾼다.
            Vector3 wish = ScreenToWorldDirection(moveInput);
            float targetSpeed = IsCrawling ? crawlSpeed : walkSpeed;

            // 목표 속도로 순간이동시키지 않고 조금씩 다가가게 한다. 그래야 움직임이 부드럽다.
            // Time.deltaTime = 지난 프레임에서 지금까지 걸린 시간(초).
            // 이걸 곱해야 프레임이 몇이든 같은 속도로 움직인다. 안 곱하면 좋은 컴퓨터에서 더 빨라진다.
            currentVelocity = Vector3.MoveTowards(
                currentVelocity, wish * targetSpeed, acceleration * Time.deltaTime);

            // 입력이 있을 때만 몸을 돌린다. 입력이 없을 때 돌리면 방향이 0이라 계산이 깨진다.
            if (wish.sqrMagnitude > 0.0001f)
            {
                // Quaternion = 회전을 담는 자료형. 각도를 직접 더하면 짐벌락이라는 문제가 생겨서
                // 유니티는 회전을 이걸로 다룬다. 직접 계산할 일은 거의 없고 함수만 쓰면 된다.
                Quaternion look = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, turnSpeedDeg * Time.deltaTime);
            }

            // 중력. 바닥에 닿아 있으면 살짝 아래로 눌러주고(-2), 공중이면 점점 빠르게 떨어진다.
            // 바닥에서도 0이 아니라 -2를 주는 이유는, 0이면 경사면에서 붕 뜨기 때문이다.
            verticalVelocity = cc.isGrounded ? -2f : verticalVelocity + Physics.gravity.y * Time.deltaTime;

            // 수평 속도(currentVelocity)와 수직 속도를 합쳐 한 번에 움직인다.
            Vector3 motion = currentVelocity;
            motion.y = verticalVelocity;
            cc.Move(motion * Time.deltaTime);   // 실제로 움직이는 건 이 한 줄뿐이다

            UpdateBodyVisual();
            UpdateFootstepNoise();
            UpdateHideSpotInteraction();
        }

        /// <summary>은신처 안에 있을 때의 한 프레임. 움직이지 않고 H만 기다린다.</summary>
        void TickHidden()
        {
            IsMoving = false;
            currentVelocity = Vector3.zero;
            noiseTimer = 0f;

            UpdateBodyVisual();

            if (HidePressed()) ExitHideSpot();
        }

        // ------------------------------------------------------------ 은신처

        /// <summary>근처에 은신처가 있는지 매 프레임 확인하고, H를 누르면 들어간다.</summary>
        void UpdateHideSpotInteraction()
        {
            NearbyHideSpot = HideSpot.Nearest(transform.position);
            if (NearbyHideSpot == null) return;     // 근처에 없으면 할 일 없음
            if (!HidePressed()) return;             // 있어도 H를 안 눌렀으면 할 일 없음

            EnterHideSpot(NearbyHideSpot);
        }

        /// <summary>은신처 안으로 들어간다. 위치를 통 한가운데로 옮겨 "들어갔다"를 눈에 보이게 한다.</summary>
        void EnterHideSpot(HideSpot spot)
        {
            CurrentHideSpot = spot;
            spot.IsOccupied = true;     // 은신처 쪽에도 "누가 들어왔다"고 알려준다
            IsCrawling = false;

            MoveTo(spot.InsidePoint, transform.rotation);
        }

        /// <summary>은신처에서 나온다. 통의 트인 방향으로 밀어내고 그쪽을 보게 한다.</summary>
        void ExitHideSpot()
        {
            HideSpot spot = CurrentHideSpot;
            CurrentHideSpot = null;     // 먼저 비워둔다. 아래에서 예외가 나도 갇히지 않게
            if (spot == null) return;

            spot.IsOccupied = false;

            Vector3 exit = spot.ExitPoint;
            Vector3 facing = exit - spot.InsidePoint;   // 벡터 뺄셈 = "여기서 저기로 가는 방향"
            facing.y = 0f;                              // 위아래 성분은 버린다. 바닥에서만 도니까

            Quaternion rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing, Vector3.up)
                : transform.rotation;                   // 방향이 0이면 지금 각도를 그대로 유지

            MoveTo(exit, rotation);
        }

        // ------------------------------------------------------------ 입력

        /// <summary>
        /// 키보드를 읽어 이동 입력과 웅크리기 여부를 돌려준다.
        /// static 인 이유: 이 함수는 플레이어의 상태를 전혀 쓰지 않고 키보드만 본다.
        /// </summary>
        static void ReadInput(out Vector2 move, out bool crawl)
        {
            // out 매개변수는 함수를 나가기 전에 반드시 값이 채워져 있어야 한다. 먼저 기본값을 넣는다.
            move = Vector2.zero;
            crawl = false;

            // Keyboard.current = 지금 쓰이고 있는 키보드. 없을 수도 있어서(패드만 연결 등) 확인한다.
            var kb = Keyboard.current;
            if (kb == null) return;

            // isPressed = "지금 눌려 있는가". 누르고 있는 동안 계속 true.
            // y 는 앞뒤, x 는 좌우. 화살표 키도 같이 받아준다.
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;

            // 대각선 보정. W+D 를 같이 누르면 길이가 1.414가 되어 대각선이 더 빨라진다.
            // Normalize 로 길이를 1로 맞춰 어느 방향이든 같은 속도가 되게 한다.
            if (move.sqrMagnitude > 1f) move.Normalize();

            crawl = kb.cKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        }

        /// <summary>
        /// H를 이번 프레임에 "새로" 눌렀는가.
        /// isPressed(누르고 있는 동안 계속 true)와 달리 wasPressedThisFrame 은 누른 순간 딱 한 프레임만
        /// true다. 토글에는 이걸 써야 한다. isPressed 를 쓰면 누르고 있는 동안 들어갔다 나갔다 반복한다.
        /// </summary>
        static bool HidePressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.hKey.wasPressedThisFrame;
        }

        /// <summary>
        /// 화면 기준 입력을 월드 기준 방향으로 바꾼다.
        /// 카메라가 45도 돌아가 있어서, W를 누르면 월드의 +Z가 아니라 "화면에서 위쪽"으로 가야 한다.
        /// </summary>
        Vector3 ScreenToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            // 카메라가 없을 때를 대비해 기본 방향을 준비해 둔다.
            Vector3 forward = isoCam != null ? isoCam.FlatForward : Vector3.forward;
            Vector3 right = isoCam != null ? isoCam.FlatRight : Vector3.right;

            // 앞 방향 × 위아래입력 + 오른 방향 × 좌우입력 = 실제로 가야 할 방향
            Vector3 dir = forward * input.y + right * input.x;

            // normalized = 길이를 1로 맞춘 같은 방향의 벡터. 방향만 필요할 때 쓴다.
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

            // 레이캐스트 = 한 점에서 한 방향으로 보이지 않는 광선을 쏴서 뭐가 맞는지 보는 것.
            // RaycastNonAlloc 은 결과를 미리 만들어 둔 배열(coverBuffer)에 담아 쓰레기를 안 만든다.
            //   origin 에서 / Vector3.up(위) 방향으로 / 1.2m 까지 /
            //   ~0 = 모든 레이어를 대상으로 / 트리거(통과 가능한 영역)는 무시
            // 돌려주는 값은 "몇 개 맞았는가".
            int count = Physics.RaycastNonAlloc(origin, Vector3.up, coverBuffer, 1.2f,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = coverBuffer[i].collider;

                // 내 몸이 맞은 건 무시한다. 이걸 빼면 항상 "가려져 있다"가 되어버린다.
                if (col == null || col.GetComponentInParent<PlayerController>() == this) continue;

                IsUnderCover = true;
                return;     // 하나만 찾으면 충분하므로 바로 끝낸다
            }

            IsUnderCover = false;
        }

        // ------------------------------------------------------------ 표현

        /// <summary>
        /// 눈에 보이는 부분을 상태에 맞춰 바꾼다. 게임 규칙에는 영향이 없고 보기용이다.
        /// 값을 즉시 바꾸지 않고 Lerp 로 조금씩 옮겨서 웅크리는 동작이 부드럽게 보이게 한다.
        /// </summary>
        void UpdateBodyVisual()
        {
            // 숨어 있을 때도 기어갈 때와 같은 낮은 실루엣을 쓴다.
            bool low = IsCrawling || IsHidden;

            // Lerp(a, b, t) = a와 b 사이를 t 비율만큼 간 값. 매 프레임 조금씩 b로 다가간다.
            // 12f * Time.deltaTime 이 그 "조금씩"의 양이다. 숫자가 클수록 빨리 바뀐다.
            float targetHeight = low ? crawlHeight : standHeight;
            cc.height = Mathf.Lerp(cc.height, targetHeight, 12f * Time.deltaTime);

            // 캡슐의 중심을 높이의 절반으로 맞춰야 발이 바닥에 붙어 있는다.
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);

            if (body != null)
            {
                // 구조체(Vector3)는 값이 복사되기 때문에 body.localScale.y = ... 처럼 바로 못 고친다.
                // 통째로 꺼내 → 고치고 → 다시 넣어야 한다. 유니티에서 아주 자주 쓰는 패턴이다.
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

                // 숨어 있을 때는 코 막대를 꺼서 통 밖으로 삐져나오지 않게 한다.
                facingMarker.gameObject.SetActive(!IsHidden);
            }
        }

        /// <summary>
        /// 걸을 때 일정 간격으로 소리를 낸다. 경비가 그 소리를 듣고 찾아온다.
        /// 지금은 walkNoiseRadius 가 0이라 실질적으로 꺼져 있다.
        /// </summary>
        void UpdateFootstepNoise()
        {
            float radius = IsCrawling ? 0f : walkNoiseRadius;   // 기어갈 때는 소리가 안 난다

            if (!IsMoving || radius <= 0f)
            {
                noiseTimer = 0f;    // 멈추면 타이머를 초기화해서, 다시 걸으면 즉시 한 발 소리가 나게
                return;
            }

            // 타이머를 매 프레임 줄이다가 0 이하가 되면 한 번 실행하고 다시 채운다.
            // 유니티에서 "일정 간격마다 무언가 하기"의 가장 기본 형태다.
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
            cc.enabled = false;                                 // 끄고
            transform.SetPositionAndRotation(position, rotation); // 옮기고
            cc.enabled = true;                                  // 다시 켠다

            // 남아 있던 속도를 지운다. 안 지우면 순간이동한 자리에서 계속 미끄러진다.
            currentVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        /// <summary>붙잡혀서 시작 지점으로 되돌릴 때 사용.</summary>
        // public 이라 StealthGameManager 같은 다른 스크립트가 부를 수 있다.
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // 은신처 안에서 붙잡혔을 수도 있다. 그 자리를 비워주지 않으면
            // 아무도 없는 은신처가 "사용 중"으로 남아 다시는 못 들어간다.
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
