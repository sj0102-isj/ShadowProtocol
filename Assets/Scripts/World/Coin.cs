using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 던진 동전. 실제 물리로 날아가 벽과 선반에 튕긴다.
    /// 멈추는 순간 그 자리에서 소음을 내므로, 벽에 각도를 맞춰 튕겨 보내는 것까지가 실력이다.
    ///
    /// 매번 만들지 않고 CoinThrow 가 하나를 재사용한다.
    /// </summary>
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

        public bool InFlight { get; private set; }
        public bool Landed { get; private set; }

        Rigidbody body;
        float flightTimer;
        float idleTimer;
        Vector3 lastVelocity;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// from 에서 to 를 향해 던진다. to 는 "가로막는 것이 없다면 떨어질 자리"일 뿐,
        /// 도중에 벽을 맞으면 튕겨서 다른 곳에 멈춘다.
        /// </summary>
        public void Throw(Vector3 from, Vector3 to)
        {
            transform.position = from;
            transform.rotation = Random.rotation;
            gameObject.SetActive(true);

            flightTimer = 0f;
            idleTimer = 0f;
            InFlight = true;
            Landed = false;

            body.isKinematic = false;
            body.linearVelocity = BallisticVelocity(from, to, flightSeconds);
            body.angularVelocity = Random.insideUnitSphere * 14f;
            lastVelocity = body.linearVelocity;
        }

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
        void OnCollisionEnter(Collision collision)
        {
            if (!InFlight || body.isKinematic) return;
            if (collision.contactCount == 0) return;

            Vector3 normal = collision.GetContact(0).normal;

            // 위를 향한 면 = 바닥이나 소품 윗면. 여기 닿았으면 비행 끝.
            if (normal.y > 0.5f)
            {
                Settle();
                return;
            }

            body.linearVelocity = Vector3.Reflect(lastVelocity, normal) * bounciness;
            lastVelocity = body.linearVelocity;
        }

        /// <summary>가로막는 것이 없다면 t초 뒤에 정확히 to 에 떨어지는 초기 속도.</summary>
        static Vector3 BallisticVelocity(Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Max(0.05f, t);

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

            idleTimer += Time.deltaTime;
            if (idleTimer >= lifetimeAfterLanding) Despawn();
        }

        /// <summary>멈춘 자리에서 소리를 낸다. 튕겨서 엉뚱한 곳에 갔어도 그 자리가 기준이다.</summary>
        void Settle()
        {
            InFlight = false;
            Landed = true;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Vector3 at = transform.position;

            // 소리 파문은 그대로 방송하되(화면 표시용), 실제로 주우러 오는 경비는
            // 여기서 한 명만 정한다. 경비들이 각자 판단하면 전원이 몰려온다.
            NoiseSystem.Emit(at, noiseRadius, noiseStrength, transform);
            GuardAI.AlertNearestToCoin(at, noiseRadius, this);
        }

        /// <summary>경비가 주웠을 때.</summary>
        public void PickUp() => Despawn();

        void Despawn()
        {
            InFlight = false;
            Landed = false;
            if (body != null) body.isKinematic = true;
            gameObject.SetActive(false);
        }
    }
}
