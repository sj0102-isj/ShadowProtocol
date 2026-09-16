using UnityEngine;
using UnityEngine.Rendering;   // AmbientMode

namespace Stealth
{
    /// <summary>
    /// 정전. 두꺼비집 레버를 내리면 맵 전체가 어두워지고 플레이어 주변만 좁게 보인다.
    /// 그동안 경비는 플레이어를 전혀 인지하지 못한다.
    ///
    /// 조명을 실제로 끄는 방식이라 화면 전체가 바뀐다.
    /// 원래 조명 설정은 켜질 때를 위해 보관해 둔다.
    /// </summary>
    //
    // ── 이 스크립트의 핵심 패턴: 저장 → 변경 → 복원 ──
    // RenderSettings 같은 전역 설정을 건드리면, 에디터에서 플레이를 멈춰도 그 값이 그대로 남는다.
    // 그래서 CaptureLighting() 으로 원본을 저장해 두고 OnDestroy 에서 RestoreLighting() 한다.
    // 이 짝을 빠뜨리면 "게임을 한 번 돌렸더니 씬이 영영 어두워졌다"가 된다.
    public class BlackoutSystem : MonoBehaviour
    {
        public static BlackoutSystem Instance { get; private set; }   // 싱글턴

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
        // 이 값은 서서히 변한다. 반면 IsActive 는 즉시 바뀐다. 둘을 나눈 이유는
        // "게임 규칙(안 보인다)"은 즉시 적용돼야 하고 "화면(어두워진다)"은 부드러워야 하기 때문이다.
        public float Darkness { get; private set; }

        /// <summary>정전이 진행 중인가. 이 동안 경비는 플레이어를 볼 수 없다.</summary>
        public bool IsActive { get; private set; }

        /// <summary>남은 시간(초). HUD 카운트다운용.</summary>
        public float Remaining { get; private set; }

        /// <summary>어디서든 간단히 확인할 수 있게.</summary>
        // GuardVision, SurveillanceDrone 이 매 프레임 이걸 본다.
        // Instance != null 검사를 여기 한 번만 써두면 부르는 쪽이 매번 안 써도 된다.
        public static bool PlayerIsInvisible => Instance != null && Instance.IsActive;

        Light sun;                          // 씬의 방향광(태양)
        float sunIntensity;                 // 원래 밝기
        Color originalAmbient;              // 원래 주변광 색
        AmbientMode originalAmbientMode;    // 원래 주변광 방식
        float originalAmbientIntensity;     // 원래 주변광 세기
        bool captured;                      // 원본을 저장했는가

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreLighting();   // 반드시 되돌린다. 안 그러면 에디터 씬이 어두운 채로 남는다
        }

        void Start()
        {
            // 씬의 모든 Light 중 방향광(태양)을 찾는다.
            // foreach = 컬렉션의 원소를 하나씩 꺼내는 반복문. for 보다 읽기 쉽다.
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                sun = light;
                break;   // 하나만 찾으면 되므로 즉시 종료
            }

            CaptureLighting();

            if (playerLight != null)
            {
                playerLight.range = playerLightRange;
                playerLight.intensity = 0f;
                playerLight.enabled = false;   // 평소에는 꺼둔다
            }
        }

        /// <summary>원래 조명 설정을 저장한다. 한 번만 실행된다.</summary>
        void CaptureLighting()
        {
            if (captured) return;   // 두 번 저장하면 "이미 어두워진 값"을 원본으로 알게 된다

            sunIntensity = sun != null ? sun.intensity : 1f;
            originalAmbient = RenderSettings.ambientLight;
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            captured = true;
        }

        /// <summary>저장해 둔 원래 설정으로 되돌린다.</summary>
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
            // 이미 정전 중이면 소리를 또 내지 않는다. 시간만 다시 채운다.
            if (!IsActive) GameAudio.Play(Sfx.BlackoutOn);

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
                    GameAudio.Play(Sfx.BlackoutOff);   // 전원이 돌아오는 소리
                }
            }

            // IsActive(즉시 켜짐/꺼짐)를 Darkness(서서히 변함)로 옮긴다.
            float target = IsActive ? 1f : 0f;
            float step = Time.deltaTime / Mathf.Max(0.01f, fadeSeconds);
            Darkness = Mathf.MoveTowards(Darkness, target, step);

            ApplyLighting();
        }

        /// <summary>Darkness(0~1)에 따라 실제 조명 값을 보간해 적용한다.</summary>
        void ApplyLighting()
        {
            if (!captured) return;

            // 태양 밝기를 원래값 ↔ 원래값×0.04 사이에서 Darkness 비율만큼 섞는다.
            if (sun != null) sun.intensity = Mathf.Lerp(sunIntensity, sunIntensity * darkenedLight, Darkness);

            // 주변광(그림자 속 밝기)도 같이 낮춘다. 이걸 안 하면 태양만 꺼지고
            // 물체들이 회색으로 둥둥 떠 있는 것처럼 보인다.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = Mathf.Lerp(originalAmbientIntensity, 1f, Darkness);
            RenderSettings.ambientLight = Color.Lerp(AmbientBaseColor(), darkenedAmbient, Darkness);

            if (playerLight == null) return;

            // 플레이어 주변만 밝히는 조명. 어두워질수록 세진다.
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
