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
        public Transform target;

        [Header("Rig")]
        [Tooltip("내려다보는 각도. 55도 정도가 시야 콘 가독성과 벽 가림의 균형점.")]
        public float pitch = 55f;
        public float yaw = 45f;
        public float distance = 22f;

        [Header("Follow")]
        public float followLerp = 8f;
        [Tooltip("플레이어가 바라보는 쪽을 살짝 미리 보여준다.")]
        public float lookAhead = 1.5f;

        Camera cam;
        Vector3 currentFocus;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Start()
        {
            if (target == null)
            {
                var player = Object.FindFirstObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }

            if (target != null)
            {
                currentFocus = target.position;
                ApplyPosition();
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = target.position + target.forward * lookAhead;
            currentFocus = Vector3.Lerp(currentFocus, desired, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            ApplyPosition();
        }

        void ApplyPosition()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(currentFocus - rot * Vector3.forward * distance, rot);
        }

        /// <summary>입력을 화면 기준으로 변환할 때 쓰는 기준 축.</summary>
        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
            }
        }

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
