using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 오크통·나무 상자 더미로 만든 은신처. E를 눌러 들어가고, 다시 E를 눌러 나온다.
    /// 안에 있는 동안 경비 시야에서 완전히 제외된다.
    ///
    /// 웅크리기(C)와는 분리된 기능이다. C는 크롤 통로를 지나는 용도이고,
    /// 숨는 것은 이 오브젝트와의 상호작용이다.
    /// </summary>
    public class HideSpot : MonoBehaviour
    {
        public static readonly List<HideSpot> All = new List<HideSpot>();

        [Header("Interaction")]
        [Tooltip("이 거리 안에서 E를 누르면 들어간다.")]
        public float interactRange = 1.5f;
        public string displayName = "오크통 더미";

        [Tooltip("나올 때 밀려나는 방향(월드 기준). 소품이 트인 쪽으로 빌더가 설정한다.")]
        public Vector3 exitDirection = Vector3.forward;
        public float exitDistance = 1.3f;

        [Header("Visual")]
        public Renderer patchRenderer;
        [Tooltip("멀리 있을 때. 바닥에 원이 떠 있으면 어색하므로 알파 0.")]
        public Color idleColor = new Color(0.35f, 0.75f, 1f, 0f);
        [Tooltip("가까이 왔을 때 — 여기 숨을 수 있다는 표시")]
        public Color nearColor = new Color(0.35f, 0.8f, 1f, 0.30f);
        [Tooltip("안에 들어가 있을 때")]
        public Color occupiedColor = new Color(0.3f, 1f, 0.65f, 0.45f);

        /// <summary>플레이어가 이 안에 들어와 있는가.</summary>
        public bool IsOccupied { get; set; }

        public Vector3 InsidePoint => transform.position;
        public Vector3 ExitPoint => transform.position + ExitDirectionNormalized * exitDistance;

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
            if (patchRenderer == null) patchRenderer = GetComponentInChildren<Renderer>();
        }

        void Start() => player = Object.FindFirstObjectByType<PlayerController>();

        void Update()
        {
            if (patchRenderer == null) return;

            Color target = idleColor;

            if (IsOccupied)
            {
                target = occupiedColor;
                // 숨어 있는 동안 은은하게 맥동시켜 "지금 안전하다"를 계속 알려준다.
                target.a *= 0.75f + Mathf.PingPong(Time.time * 0.6f, 0.35f);
            }
            else if (player != null && InRange(player.transform.position))
            {
                target = nearColor;
            }

            patchRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, target);
            patchRenderer.SetPropertyBlock(mpb);
        }

        public bool InRange(Vector3 worldPosition)
        {
            var a = new Vector2(worldPosition.x, worldPosition.z);
            var b = new Vector2(transform.position.x, transform.position.z);
            return (a - b).sqrMagnitude <= interactRange * interactRange;
        }

        /// <summary>가장 가까운, 들어갈 수 있는 은신처.</summary>
        public static HideSpot Nearest(Vector3 worldPosition)
        {
            HideSpot best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < All.Count; i++)
            {
                HideSpot spot = All[i];
                if (spot == null || !spot.InRange(worldPosition)) continue;

                float d = (spot.transform.position - worldPosition).sqrMagnitude;
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = spot;
            }

            return best;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.65f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, ExitPoint);
        }
    }
}
