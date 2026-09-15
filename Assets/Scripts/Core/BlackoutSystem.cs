using UnityEngine;
using UnityEngine.Rendering;

namespace Stealth
{
    /// <summary>
    /// 정전. 두꺼비집 레버를 내리면 맵 전체가 어두워지고 플레이어 주변만 좁게 보인다.
    /// 그동안 경비는 플레이어를 전혀 인지하지 못한다.
    ///
    /// 조명을 실제로 끄는 방식이라 화면 전체가 바뀐다.
    /// 원래 조명 설정은 켜질 때를 위해 보관해 둔다.
    /// </summary>
    public class BlackoutSystem : MonoBehaviour
    {
        public static BlackoutSystem Instance { get; private set; }

        [Header("Timing")]
        public float duration = 10f;
        [Tooltip("어두워지고 밝아지는 데 걸리는 시간.")]
        public float fadeSeconds = 0.35f;

        [Header("Darkness")]
        [Tooltip("정전 중 전역 조명 밝기 배율. 0에 가까울수록 깜깜해진다.")]
        [Range(0f, 0.5f)] public float darkenedLight = 0.04f;
        [Tooltip("정전 중 주변광 색. 완전한 검정보다 아주 살짝 푸른 편이 보기 좋다.")]
        public Color darkenedAmbient = new Color(0.012f, 0.015f, 0.03f);

        [Header("Player Light")]
        [Tooltip("정전 중 플레이어 주변을 비추는 조명. 빌더가 연결한다.")]
        public Light playerLight;
        public float playerLightRange = 7f;
        public float playerLightIntensity = 2.2f;

        /// <summary>0 = 평소, 1 = 완전 정전. 연출 보간에 쓴다.</summary>
        public float Darkness { get; private set; }

        /// <summary>정전이 진행 중인가. 이 동안 경비는 플레이어를 볼 수 없다.</summary>
        public bool IsActive { get; private set; }

        /// <summary>남은 시간(초). HUD 카운트다운용.</summary>
        public float Remaining { get; private set; }

        /// <summary>어디서든 간단히 확인할 수 있게.</summary>
        public static bool PlayerIsInvisible => Instance != null && Instance.IsActive;

        Light sun;
        float sunIntensity;
        Color originalAmbient;
        AmbientMode originalAmbientMode;
        float originalAmbientIntensity;
        bool captured;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreLighting();
        }

        void Start()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                sun = light;
                break;
            }

            CaptureLighting();

            if (playerLight != null)
            {
                playerLight.range = playerLightRange;
                playerLight.intensity = 0f;
                playerLight.enabled = false;
            }
        }

        void CaptureLighting()
        {
            if (captured) return;

            sunIntensity = sun != null ? sun.intensity : 1f;
            originalAmbient = RenderSettings.ambientLight;
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            captured = true;
        }

        void RestoreLighting()
        {
            if (!captured) return;

            if (sun != null) sun.intensity = sunIntensity;
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbient;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
        }

        /// <summary>레버가 호출한다. 이미 정전 중이면 시간만 다시 채운다.</summary>
        public void Trigger()
        {
            IsActive = true;
            Remaining = duration;
        }

        void Update()
        {
            if (IsActive)
            {
                Remaining -= Time.deltaTime;
                if (Remaining <= 0f)
                {
                    Remaining = 0f;
                    IsActive = false;
                }
            }

            float target = IsActive ? 1f : 0f;
            float step = Time.deltaTime / Mathf.Max(0.01f, fadeSeconds);
            Darkness = Mathf.MoveTowards(Darkness, target, step);

            ApplyLighting();
        }

        void ApplyLighting()
        {
            if (!captured) return;

            if (sun != null) sun.intensity = Mathf.Lerp(sunIntensity, sunIntensity * darkenedLight, Darkness);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = Mathf.Lerp(originalAmbientIntensity, 1f, Darkness);
            RenderSettings.ambientLight = Color.Lerp(AmbientBaseColor(), darkenedAmbient, Darkness);

            if (playerLight == null) return;

            playerLight.enabled = Darkness > 0.01f;
            playerLight.intensity = playerLightIntensity * Darkness;
            playerLight.range = playerLightRange;
        }

        /// <summary>
        /// 원래 주변광. Flat 이 아니었다면 근사값을 쓴다.
        /// 정전이 끝나면 OnDestroy 에서 원본 설정을 그대로 되돌린다.
        /// </summary>
        Color AmbientBaseColor() =>
            originalAmbientMode == AmbientMode.Flat ? originalAmbient : RenderSettings.ambientSkyColor;
    }
}
