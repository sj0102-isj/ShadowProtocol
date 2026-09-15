using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 쿼터뷰(아이소메트릭) 카메라. 고정 각도로 플레이어를 부드럽게 따라간다.
    /// 각도를 고정해야 경비 시야 콘이 항상 같은 방식으로 읽힌다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;   // 따라갈 대상. 비어 있으면 Start 에서 플레이어를 찾는다

        [Header("Rig")]
        [Tooltip("내려다보는 각도. 55도 정도가 시야 콘 가독성과 벽 가림의 균형점.")]
        public float pitch = 55f;     // 위아래 각도 (X축 회전)
        public float yaw = 45f;       // 좌우 각도 (Y축 회전). 45도라 대각선에서 본다
        public float distance = 22f;  // 대상에서 얼마나 떨어져 있는가

        [Header("Follow")]
        public float followLerp = 8f;   // 따라가는 빠르기. 클수록 즉각적
        [Tooltip("플레이어가 바라보는 쪽을 살짝 미리 보여준다.")]
        public float lookAhead = 1.5f;

        Camera cam;
        Vector3 currentFocus;   // 카메라가 지금 보고 있는 지점. 목표로 서서히 다가간다

        void Awake()
        {
            cam = GetComponent<Camera>();

            // orthographic(직교) 투영 = 원근이 없다. 멀리 있는 것도 같은 크기로 보인다.
            // 쿼터뷰 게임에서 이걸 쓰는 이유는, 원근이 있으면 화면 가장자리의 시야 콘이
            // 찌그러져 보여서 "이 안에 들어가면 들킨다"를 눈으로 가늠하기 어려워지기 때문이다.
            cam.orthographic = true;
            cam.orthographicSize = 10f;   // 화면 세로 절반이 월드에서 몇 m인가

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Start()
        {
            if (target == null)
            {
                var player = Object.FindFirstObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }

            // 첫 프레임에는 보간 없이 곧바로 자리를 잡는다.
            // 안 그러면 게임 시작 순간 카메라가 원점에서 플레이어까지 쭉 날아간다.
            if (target != null)
            {
                currentFocus = target.position;
                ApplyPosition();
            }
        }

        /// <summary>
        /// LateUpdate 에서 카메라를 옮기는 것이 원칙이다.
        /// Update 에서 옮기면 플레이어가 아직 안 움직인 상태의 위치를 따라가서 한 프레임 떨린다.
        /// </summary>
        void LateUpdate()
        {
            if (target == null) return;

            // 바라보는 쪽을 조금 앞서 보여주면 진행 방향의 정보를 더 볼 수 있다.
            Vector3 desired = target.position + target.forward * lookAhead;

            // 1 - Exp(-k·dt) 는 프레임 속도와 무관하게 같은 감속 곡선을 만드는 보간 계수다.
            // 그냥 Lerp(a, b, k * dt) 를 쓰면 프레임이 높은 컴퓨터에서 더 빨리 따라붙는다.
            currentFocus = Vector3.Lerp(currentFocus, desired, 1f - Mathf.Exp(-followLerp * Time.deltaTime));

            ApplyPosition();
        }

        /// <summary>바라보는 지점에서 고정 각도·거리만큼 뒤로 물러난 자리에 카메라를 놓는다.</summary>
        void ApplyPosition()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            // rot * Vector3.forward = 그 각도에서의 '앞' 방향.
            // 초점에서 그 반대로 distance 만큼 물러나면 초점을 바라보는 자리가 된다.
            transform.SetPositionAndRotation(currentFocus - rot * Vector3.forward * distance, rot);
        }

        /// <summary>입력을 화면 기준으로 변환할 때 쓰는 기준 축.</summary>
        //
        // 카메라가 45도 기울어 있어서, W를 눌렀을 때 월드의 +Z 가 아니라 "화면에서 위쪽"으로
        // 가야 한다. 그 '화면에서 위쪽'에 해당하는 월드 방향이 이 값이다.
        // 카메라는 아래를 내려다보고 있으므로 y 성분을 버리고 평평하게 만든다.
        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
            }
        }

        /// <summary>화면 기준 오른쪽에 해당하는 월드 방향.</summary>
        public Vector3 FlatRight
        {
            get
            {
                Vector3 r = transform.right;
                r.y = 0f;
                return r.sqrMagnitude < 0.0001f ? Vector3.right : r.normalized;
            }
        }
    }
}
