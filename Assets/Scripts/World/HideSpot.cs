using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 오크통·나무 상자 더미로 만든 은신처. H를 눌러 들어가고, 다시 H를 눌러 나온다.
    /// 안에 있는 동안 경비 시야에서 완전히 제외된다.
    ///
    /// 웅크리기(C)와는 분리된 기능이다. C는 크롤 통로를 지나는 용도이고,
    /// 숨는 것은 이 오브젝트와의 상호작용이다.
    /// </summary>
    //
    // 실제로 "들어가고 나오는" 동작은 PlayerController 가 한다(EnterHideSpot/ExitHideSpot).
    // 이 스크립트는 "어디에 있고, 사거리가 얼마이고, 어느 쪽으로 나오는가"를 들고 있는 데이터에 가깝다.
    // 이렇게 나누면 입력 처리는 한 곳(플레이어)에만 있게 되어 흐름을 쫓기 쉽다.
    public class HideSpot : MonoBehaviour
    {
        // 씬에 있는 모든 은신처 명단. Nearest() 가 이걸 훑는다.
        public static readonly List<HideSpot> All = new List<HideSpot>();

        [Header("Interaction")]
        [Tooltip("이 거리 안에서 E를 누르면 들어간다.")]
        public float interactRange = 1.5f;
        public string displayName = "오크통 더미";

        [Tooltip("나올 때 밀려나는 방향(월드 기준). 소품이 트인 쪽으로 빌더가 설정한다.")]
        public Vector3 exitDirection = Vector3.forward;
        public float exitDistance = 1.3f;

        [Header("Visual")]
        public Renderer patchRenderer;   // 바닥에 깔린 원형 표시
        [Tooltip("멀리 있을 때. 바닥에 원이 떠 있으면 어색하므로 알파 0.")]
        public Color idleColor = new Color(0.35f, 0.75f, 1f, 0f);
        [Tooltip("가까이 왔을 때 — 여기 숨을 수 있다는 표시")]
        public Color nearColor = new Color(0.35f, 0.8f, 1f, 0.30f);
        [Tooltip("안에 들어가 있을 때")]
        public Color occupiedColor = new Color(0.3f, 1f, 0.65f, 0.45f);

        /// <summary>플레이어가 이 안에 들어와 있는가.</summary>
        // { get; set; } — 밖에서 쓰기도 가능하다. PlayerController 가 들어가고 나올 때 이 값을 바꾼다.
        public bool IsOccupied { get; set; }

        /// <summary>들어갔을 때 서게 되는 자리 = 통 더미 한가운데.</summary>
        public Vector3 InsidePoint => transform.position;

        /// <summary>나올 때 밀려나는 자리 = 트인 방향으로 exitDistance 만큼.</summary>
        public Vector3 ExitPoint => transform.position + ExitDirectionNormalized * exitDistance;

        // exitDirection 을 인스펙터에서 (0,0,0) 으로 두면 방향이 없어 계산이 깨진다.
        // 그럴 때 앞쪽을 기본값으로 쓰도록 한 겹 감싼 프로퍼티.
        Vector3 ExitDirectionNormalized =>
            exitDirection.sqrMagnitude < 0.0001f ? Vector3.forward : exitDirection.normalized;

        PlayerController player;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();

            // 인스펙터에서 안 꽂아줬으면 자식에서 알아서 찾는다.
            if (patchRenderer == null) patchRenderer = GetComponentInChildren<Renderer>();
        }

        void Start() => player = Object.FindFirstObjectByType<PlayerController>();

        /// <summary>바닥 표시의 색만 갱신한다. 게임 규칙은 건드리지 않는다.</summary>
        void Update()
        {
            if (patchRenderer == null) return;

            Color target = idleColor;   // 기본은 투명 (알파 0)

            if (IsOccupied)
            {
                target = occupiedColor;

                // 숨어 있는 동안 은은하게 맥동시켜 "지금 안전하다"를 계속 알려준다.
                // PingPong(t, len) = 0 ↔ len 사이를 왕복하는 값. Sin 과 달리 음수가 없다.
                target.a *= 0.75f + Mathf.PingPong(Time.time * 0.6f, 0.35f);
            }
            else if (player != null && InRange(player.transform.position))
            {
                target = nearColor;   // 가까이 오면 보이게
            }

            patchRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, target);
            patchRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>그 위치가 사거리 안인가. 높이는 무시하고 바닥 평면 거리만 본다.</summary>
        public bool InRange(Vector3 worldPosition)
        {
            // Vector2 에 x 와 z 를 담으면 위에서 내려다본 평면 좌표가 된다.
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);

            // 양쪽을 제곱해서 비교하면 제곱근 계산을 생략할 수 있다. 결과는 같다.
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        /// <summary>가장 가까운, 들어갈 수 있는 은신처.</summary>
        // static 이라 HideSpot.Nearest(...) 처럼 클래스 이름으로 부른다.
        // 특정 은신처의 일이 아니라 "전체 중에서 고르는" 일이기 때문이다.
        public static HideSpot Nearest(Vector3 worldPosition)
        {
            HideSpot best = null;
            float bestDistance = float.MaxValue;   // "아직 못 찾음"을 뜻하는 초기값

            for (int i = 0; i < All.Count; i++)
            {
                HideSpot spot = All[i];
                if (spot == null || !spot.InRange(worldPosition)) continue;

                float d = (spot.transform.position - worldPosition).sqrMagnitude;
                if (d >= bestDistance) continue;   // 지금까지 찾은 것보다 멀면 탈락

                bestDistance = d;
                best = spot;
            }

            return best;   // 사거리 안에 아무것도 없으면 null 이 돌아간다
        }

        /// <summary>씬 뷰에서 선택했을 때 사거리(초록 원)와 나오는 방향(노란 선)을 그린다.</summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.65f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, ExitPoint);
        }
    }
}
