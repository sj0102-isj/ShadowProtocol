using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 던진 동전. 실제 물리로 날아가 벽과 선반에 튕긴다.
    /// 멈추는 순간 그 자리에서 소음을 내므로, 벽에 각도를 맞춰 튕겨 보내는 것까지가 실력이다.
    ///
    /// 매번 만들지 않고 CoinThrow 가 하나를 재사용한다.
    /// </summary>
    //
    // "재사용"이 핵심 설계다. 던질 때마다 Instantiate 로 새로 만들고 Destroy 로 지우면
    // 쓰레기가 쌓여 가비지 컬렉션이 돌고 그 순간 화면이 튄다.
    // 대신 동전 하나를 미리 만들어 두고 SetActive(false) 로 숨겼다 꺼냈다 한다.
    // 이 기법을 오브젝트 풀링(object pooling)이라고 부른다.
    [RequireComponent(typeof(Rigidbody))]
    public class Coin : MonoBehaviour
    {
        [Header("Flight")]
        [Tooltip("가로막는 것이 없을 때 조준점에 떨어지기까지 걸리는 시간(초). 던지는 속도를 여기서 역산한다. " +
                 "키우면 더 높이 뜨는데, 선반(2.0m)을 넘어갈 만큼 높아지면 안 된다.")]
        public float flightSeconds = 0.85f;
        [Tooltip("벽 사이에 끼어 계속 튕기는 일이 없도록 하는 안전장치(초).")]
        public float maxFlightSeconds = 4f;

        [Header("Bounce")]
        [Tooltip("벽에 부딪힐 때 남는 속도의 비율. 바닥에는 적용되지 않는다 — 바닥에 닿으면 그 자리에서 멈춘다.")]
        [Range(0f, 1f)] public float bounciness = 0.7f;

        [Header("Noise")]
        [Tooltip("경비가 반응하는 범위(m). 직선 거리가 아니라 '걸어서 가야 하는 거리'다. " +
                 "벽 너머 경비는 돌아가는 길이 멀어서 자동으로 제외된다.")]
        public float noiseRadius = 24f;
        public float noiseStrength = 1f;

        [Header("Lifetime")]
        [Tooltip("아무도 주우러 오지 않아도 이 시간이 지나면 사라진다.")]
        public float lifetimeAfterLanding = 12f;

        /// <summary>지금 날아가는 중인가.</summary>
        public bool InFlight { get; private set; }

        /// <summary>바닥에 멈춰 있는가. 경비가 주우러 올 수 있는 상태.</summary>
        public bool Landed { get; private set; }

        Rigidbody body;         // 유니티 물리 부품. 중력·속도·충돌을 담당한다
        float flightTimer;      // 날아간 시간 (안전장치용)
        float idleTimer;        // 멈춘 뒤 흐른 시간 (수명용)
        Vector3 lastVelocity;   // 직전 프레임의 속도. 반사 계산에 쓴다

        void Awake()
        {
            body = GetComponent<Rigidbody>();

            // 시작할 때는 숨겨 둔다. 던질 때 Throw 가 다시 켠다.
            gameObject.SetActive(false);
        }

        /// <summary>
        /// from 에서 to 를 향해 던진다. to 는 "가로막는 것이 없다면 떨어질 자리"일 뿐,
        /// 도중에 벽을 맞으면 튕겨서 다른 곳에 멈춘다.
        /// </summary>
        public void Throw(Vector3 from, Vector3 to)
        {
            transform.position = from;
            transform.rotation = Random.rotation;   // 아무 각도로 시작해야 던진 느낌이 난다
            gameObject.SetActive(true);

            GameAudio.Play(Sfx.CoinThrow);

            flightTimer = 0f;
            idleTimer = 0f;
            InFlight = true;
            Landed = false;

            // isKinematic = true 면 물리 영향을 안 받는다(멈춰 있는 상태).
            // 던지는 순간 false 로 바꿔 중력과 충돌이 작동하게 한다.
            body.isKinematic = false;

            body.linearVelocity = BallisticVelocity(from, to, flightSeconds);
            body.angularVelocity = Random.insideUnitSphere * 14f;   // 빙글빙글 돌면서 날아가게
            lastVelocity = body.linearVelocity;
        }

        /// <summary>
        /// FixedUpdate — 물리 계산 주기에 맞춰 호출된다(기본 초당 50회, 프레임과 무관).
        /// Rigidbody 를 다룰 때는 Update 가 아니라 여기를 써야 물리와 박자가 맞는다.
        ///
        /// 여기서 하는 일은 "직전 속도 기억하기" 하나뿐이다. 왜 필요한지는 아래 OnCollisionEnter 참고.
        /// </summary>
        void FixedUpdate()
        {
            if (InFlight && !body.isKinematic) lastVelocity = body.linearVelocity;
        }

        /// <summary>
        /// 벽은 튕기고 바닥은 즉시 멈춘다. 바닥에서도 튕기게 하면 통통 튀다 굴러다녀서
        /// "던져서 떨어뜨린다"는 느낌이 사라진다.
        ///
        /// 반사를 직접 계산하는 이유는, 충돌이 처리된 뒤의 속도는 이미 벽 방향 성분이
        /// 죽어 있어서 그걸 반사해봐야 거의 튕기지 않기 때문이다.
        /// 직전 프레임 속도를 기억해 두었다가 그것을 반사한다.
        /// </summary>
        // OnCollisionEnter 는 유니티가 "무언가에 부딪힌 순간" 자동으로 불러주는 함수다.
        void OnCollisionEnter(Collision collision)
        {
            if (!InFlight || body.isKinematic) return;
            if (collision.contactCount == 0) return;

            // normal = 부딪힌 면이 바라보는 방향(법선 벡터). 벽이면 옆을, 바닥이면 위를 향한다.
            Vector3 normal = collision.GetContact(0).normal;

            // 위를 향한 면 = 바닥이나 소품 윗면. 여기 닿았으면 비행 끝.
            // 0.5 는 대략 60도 이내로 위를 향한다는 뜻이다.
            if (normal.y > 0.5f)
            {
                Settle();
                return;
            }

            // Vector3.Reflect(들어온 방향, 면의 법선) = 거울에 반사된 방향.
            // 거기에 bounciness(0.7)를 곱해 튕길 때마다 속도가 줄게 한다.
            body.linearVelocity = Vector3.Reflect(lastVelocity, normal) * bounciness;
            lastVelocity = body.linearVelocity;
        }

        /// <summary>가로막는 것이 없다면 t초 뒤에 정확히 to 에 떨어지는 초기 속도.</summary>
        //
        // 포물선 운동 공식을 거꾸로 푼 것이다.
        //   수평: 거리 = 속도 × 시간          →  속도 = 거리 / 시간
        //   수직: 거리 = 속도×시간 + ½·중력·시간²  →  속도 = 거리/시간 - ½·중력·시간
        // 중력이 음수(-9.81)라 빼면 위로 던지는 속도가 나온다.
        static Vector3 BallisticVelocity(Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Max(0.05f, t);   // 0으로 나누는 사고 방지

            Vector3 delta = to - from;
            Vector3 velocity = delta / t;
            velocity.y = delta.y / t - 0.5f * Physics.gravity.y * t;
            return velocity;
        }

        void Update()
        {
            if (InFlight)
            {
                // 착지는 OnCollisionEnter 가 잡는다. 여기는 벽 사이에 낀 경우의 안전장치다.
                flightTimer += Time.deltaTime;
                if (flightTimer >= maxFlightSeconds) Settle();
                return;
            }

            if (!Landed) return;

            // 아무도 안 주우면 시간이 지나 사라진다.
            idleTimer += Time.deltaTime;
            if (idleTimer >= lifetimeAfterLanding) Despawn();
        }

        /// <summary>멈춘 자리에서 소리를 낸다. 튕겨서 엉뚱한 곳에 갔어도 그 자리가 기준이다.</summary>
        void Settle()
        {
            InFlight = false;
            Landed = true;

            // 속도를 지우고 물리를 꺼서 그 자리에 고정한다. 안 그러면 미세하게 계속 굴러간다.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Vector3 at = transform.position;

            // 소리 파문은 그대로 방송하되(화면 표시용), 실제로 주우러 오는 경비는
            // 여기서 한 명만 정한다. 경비들이 각자 판단하면 전원이 몰려온다.
            // 동전이 부딪히는 소리. 매번 음높이를 흔들어야 여러 번 던져도 지겹지 않다.
            GameAudio.Play(Sfx.CoinLand, Random.Range(0.90f, 1.12f));

            NoiseSystem.Emit(at, noiseRadius, noiseStrength, transform);
            GuardAI.AlertNearestToCoin(at, noiseRadius, this);
        }

        /// <summary>경비가 주웠을 때.</summary>
        public void PickUp() => Despawn();

        /// <summary>다시 숨긴다. 파괴하지 않으므로 다음에 또 던질 수 있다.</summary>
        void Despawn()
        {
            InFlight = false;
            Landed = false;
            if (body != null) body.isKinematic = true;
            gameObject.SetActive(false);
        }
    }
}
