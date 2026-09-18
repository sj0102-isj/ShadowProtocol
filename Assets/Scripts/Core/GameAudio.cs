using UnityEngine;

namespace Stealth
{
    /// <summary>효과음 종류. 부르는 쪽은 이 이름만 알면 된다.</summary>
    public enum Sfx
    {
        Footstep,       // 걸을 때 발소리
        FootstepCrawl,  // 기어갈 때 발소리 (더 낮고 조용하게)
        Dash,           // Shift 대시
        CoinThrow,      // 동전을 던지는 순간
        CoinLand,       // 동전이 바닥·벽에 부딪히는 순간
        Collect,        // 기밀 자료 회수
        HideIn,         // 은신처에 들어감
        HideOut,        // 은신처에서 나옴
        Vent,           // 환풍구 이동
        Lever,          // 두꺼비집 레버를 내림
        BlackoutOn,     // 정전 시작 (전원이 꺼지는 소리)
        BlackoutOff,    // 정전 종료 (전원이 들어오는 소리)
        Suspicious,     // 경비가 뭔가 눈치챔 (노랑)
        Search,         // 경비가 수색 시작 (주황)
        Detected,       // 발각 (빨강)
        Capture,        // 붙잡힘
        Success,        // 탈출 성공
        Fail,           // 미션 실패
        UiClick,        // 메뉴 버튼
    }

    /// <summary>
    /// 소리 파일 없이 효과음을 <b>코드로 계산해서</b> 만들고 재생한다.
    ///
    /// 왜 파일을 안 쓰나
    /// ----------------
    /// 이 프로젝트는 외부 에셋을 하나도 쓰지 않는다는 규칙으로 만들었다.
    /// 모양을 캡슐·큐브를 겹쳐 만들었으니 소리도 같은 방식으로 만든다.
    /// 다운로드도, 라이선스 표기도, 파일 관리도 필요 없다.
    ///
    /// 소리가 뭔지
    /// ----------
    /// 소리는 결국 <b>1초에 44100번 찍은 숫자 배열</b>이다. 그 숫자가 스피커
    /// 진동판의 위치를 뜻한다. 그러니 float 배열을 직접 채워 넣으면 그게 곧 소리다.
    ///
    ///   AudioClip.Create(...)   빈 배열을 가진 클립을 만든다
    ///   clip.SetData(배열, 0)   우리가 계산한 숫자를 집어넣는다
    ///
    /// 쓰는 법
    /// ------
    ///   GameAudio.Play(Sfx.Footstep);           그냥 재생
    ///   GameAudio.Play(Sfx.Footstep, 1.05f);    살짝 높은 음으로 재생
    ///
    /// 씬에 오브젝트를 넣을 필요가 없다. 게임이 시작되면 아래 Bootstrap 이
    /// 스스로 오브젝트를 만들어 붙는다.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        /// <summary>전체 음량. 0 이면 완전 무음. 영상을 무음으로 찍고 싶으면 0 으로 둔다.</summary>
        public static float MasterVolume = 0.75f;

        // 1초에 몇 번 숫자를 찍을 것인가. CD 음질과 같은 값이다.
        const int Rate = 44100;

        // 동시에 몇 개까지 겹쳐서 낼 수 있는가.
        // 발소리마다 음높이를 조금씩 다르게 주려면 AudioSource 가 여러 개 있어야 한다.
        // (PlayOneShot 은 음높이를 따로 못 주기 때문이다)
        const int Voices = 12;

        AudioClip[] clips;      // Sfx 번호 → 미리 계산해 둔 소리
        float[] gains;          // Sfx 번호 → 이 소리의 음량
        float[] minGap;         // Sfx 번호 → 이 간격보다 빨리 또 내지 않는다
        float[] lastPlayed;     // Sfx 번호 → 마지막으로 낸 시각
        AudioSource[] voices;   // 돌려 쓰는 스피커들
        int nextVoice;          // 다음에 쓸 스피커 번호

        // --------------------------------------------------------------- 시동

        /// <summary>
        /// 씬이 열릴 때 유니티가 자동으로 불러 주는 함수다.
        /// 이것 덕분에 스테이지마다 오디오 오브젝트를 배치할 필요가 없다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Game Audio");
            DontDestroyOnLoad(go);        // 씬을 바꿔도 살아남는다
            go.AddComponent<GameAudio>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildClips();

            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;    // 2D. 쿼터뷰라 거리감을 줄 이유가 없다
                voices[i] = src;
            }
        }

        // --------------------------------------------------------------- 재생

        /// <summary>효과음을 낸다. pitch 를 1보다 크게 주면 더 높고 빠르게 들린다.</summary>
        public static void Play(Sfx sfx, float pitch = 1f)
        {
            var a = Instance;
            if (a == null || MasterVolume <= 0f) return;

            int i = (int)sfx;
            if (a.clips == null || i < 0 || i >= a.clips.Length || a.clips[i] == null) return;

            // 같은 소리가 너무 촘촘히 겹치는 것을 막는다.
            // 경비가 다섯이면 "의심" 이 한꺼번에 다섯 번 울려서 귀가 아프다.
            //
            // Time.time 이 아니라 Time.unscaledTime 을 쓰는 이유:
            // 미션이 끝나면 Time.timeScale 을 0 으로 만들어 게임을 멈추는데,
            // 그때 Time.time 도 같이 멈춰서 성공·실패 소리가 영영 막힌다.
            float now = Time.unscaledTime;
            if (now - a.lastPlayed[i] < a.minGap[i]) return;
            a.lastPlayed[i] = now;

            // 스피커를 순서대로 돌려 쓴다. 다 쓰면 처음으로 돌아간다.
            // 오래된 소리가 잘릴 수는 있지만 12개면 실제로는 거의 안 겹친다.
            AudioSource src = a.voices[a.nextVoice];
            a.nextVoice = (a.nextVoice + 1) % Voices;

            src.pitch = pitch;
            src.volume = a.gains[i] * MasterVolume;
            src.clip = a.clips[i];
            src.Play();
        }

        // --------------------------------------------------------- 소리 만들기

        void BuildClips()
        {
            int count = System.Enum.GetValues(typeof(Sfx)).Length;
            clips = new AudioClip[count];
            gains = new float[count];
            minGap = new float[count];
            lastPlayed = new float[count];

            // 아주 옛날 시각으로 채워 둔다. 0 으로 두면 게임 시작 직후의 첫 소리가
            // "방금 낸 소리" 로 오해받아 막힌다.
            for (int i = 0; i < count; i++) lastPlayed[i] = -999f;

            // 한 줄의 구성
            //   Reg( 어떤 소리 , 이름 , 파형 , 부드럽게 , 음량 )
            //
            //   부드럽게 — 0에 가까울수록 고음을 깎아 귀에 덜 거슬린다. 1.00 은 안 깎음
            //   음량     — 파형은 전부 최대치 0.9 로 맞춰 두었으므로(ToClip 참고)
            //              이 숫자만 보면 어느 소리가 더 큰지 바로 알 수 있다
            //
            // 음량을 정한 기준: 자주 나는 소리일수록 작게, 중요한 순간일수록 크게.
            // 발소리는 0.45초마다 계속 울리므로 가장 조용해야 하고,
            // 붙잡히는 소리는 판이 끝나는 순간이라 가장 커도 된다.
            //
            //   붙잡힘 > 발각 > 성공 > 자료·실패 > 정전 > 레버·환풍구·동전착지
            //   > 수색 > 의심·은신 > 동전던지기·클릭 > 발소리 > 포복
            //
            // 제일 큰 소리와 제일 작은 소리의 차이는 약 5배로 눌러 두었다.
            // 차이가 너무 크면 조용하다가 갑자기 놀라는 소리가 되어 그 자체로 거슬린다.
            //
            //                                                                     부드럽게  음량
            Reg(Sfx.Footstep,      "Footstep",      Step(0.35f, 0.22f, 105f),         0.30f, 0.260f);
            Reg(Sfx.FootstepCrawl, "FootstepCrawl", Step(0.22f, 0.08f, 72f),          0.45f, 0.148f);
            // 대시. 동전 던지기와 같은 바람 소리지만 더 길고 굵게 — 몸이 움직이는 소리다.
            Reg(Sfx.Dash,          "Dash",          Whoosh(0.26f, 0.04f, 0.18f),      0.14f, 0.300f);
            Reg(Sfx.CoinThrow,     "CoinThrow",     Whoosh(0.20f, 0.05f, 0.22f),      0.16f, 0.157f);
            Reg(Sfx.CoinLand,      "CoinLand",      Metal(),                          0.35f, 0.428f);
            Reg(Sfx.Collect,       "Collect",       Notes(Hz(523, 659, 784),
                                                    0.10f, Shape.Tri, 0.24f),         0.50f, 0.277f);
            Reg(Sfx.HideIn,        "HideIn",        Sweep(0.20f, 150f, 70f,
                                                    Shape.Sine, 18f, 0.60f, 0.25f),   1.00f, 0.187f);
            Reg(Sfx.HideOut,       "HideOut",       Sweep(0.20f, 70f, 150f,
                                                    Shape.Sine, 16f, 0.50f, 0.20f),   1.00f, 0.148f);
            Reg(Sfx.Vent,          "Vent",          Whoosh(0.55f, 0.03f, 0.26f),      0.16f, 0.284f);
            Reg(Sfx.Lever,         "Lever",         Clunk(),                          0.50f, 0.712f);
            Reg(Sfx.BlackoutOn,    "BlackoutOn",    Sweep(0.85f, 240f, 38f,
                                                    Shape.Saw, 3.2f, 0.34f, 0.10f),   0.20f, 0.295f);
            Reg(Sfx.BlackoutOff,   "BlackoutOff",   Sweep(0.60f, 45f, 250f,
                                                    Shape.Saw, 2.4f, 0.30f, 0.08f),   0.20f, 0.180f);
            Reg(Sfx.Suspicious,    "Suspicious",    Notes(Hz(700, 940),
                                                    0.07f, Shape.Tri, 0.20f),         0.50f, 0.161f);
            Reg(Sfx.Search,        "Search",        Notes(Hz(660, 620, 580),
                                                    0.09f, Shape.Tri, 0.20f),         0.50f, 0.191f);
            Reg(Sfx.Detected,      "Detected",      Notes(Hz(880, 660, 880, 660),
                                                    0.12f, Shape.Square, 0.30f),      0.22f, 0.271f);
            Reg(Sfx.Capture,       "Capture",       Sweep(1.00f, 120f, 42f,
                                                    Shape.Sine, 3.5f, 0.85f, 0.18f),  0.50f, 0.474f);
            Reg(Sfx.Success,       "Success",       Notes(Hz(523, 659, 784, 1047),
                                                    0.13f, Shape.Tri, 0.22f),         0.50f, 0.331f);
            Reg(Sfx.Fail,          "Fail",          Notes(Hz(392, 311, 262),
                                                    0.26f, Shape.Saw, 0.22f),         0.18f, 0.308f);
            Reg(Sfx.UiClick,       "UiClick",       Notes(Hz(1200),
                                                    0.035f, Shape.Tri, 0.18f),        0.60f, 0.122f);

            // 같은 소리를 다시 낼 수 있게 되기까지의 최소 간격(초).
            for (int i = 0; i < count; i++) minGap[i] = 0.06f;

            // 경비가 여럿이면 순찰 <-> 의심을 오가며 블립이 연달아 터진다.
            // 실제로 들리는 정보는 "누군가 눈치챘다" 하나뿐이라 한 번만 내면 충분하다.
            minGap[(int)Sfx.Suspicious] = 0.70f;
            minGap[(int)Sfx.Search]     = 0.70f;
            minGap[(int)Sfx.Detected]   = 0.35f;
        }

        /// <summary>계산한 파형을 다듬어서 목록에 등록한다.</summary>
        void Reg(Sfx sfx, string name, float[] data, float soften, float gain)
        {
            if (soften < 0.999f) LowPass(data, soften);

            clips[(int)sfx] = ToClip(name, data);
            gains[(int)sfx] = gain;
        }

        /// <summary>주파수 목록을 짧게 쓰기 위한 도우미. Hz(523, 659) 처럼 쓴다.</summary>
        static float[] Hz(params float[] values) => values;

        // --------------------------------------------------------------- 도구

        enum Shape { Sine, Square, Saw, Tri }

        static int Len(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));

        /// <summary>
        /// 고음을 깎는다. k 가 작을수록 많이 깎여 먹먹해진다.
        ///
        /// 원리는 "새 값 쪽으로 조금씩만 따라가기" 다. 빠르게 출렁이는 성분(고음)은
        /// 따라가기 전에 방향이 바뀌어 버려서 깎이고, 천천히 움직이는 성분(저음)만 남는다.
        ///
        /// 왜 필요한가: 사각파·톱니파·잡음은 아주 높은 음까지 잔뜩 들어 있어서
        /// 그대로 쓰면 귀를 찌른다. 실제로 재 보니 동전 던지기와 환풍구가 유난히
        /// 날카로웠는데(밝기 0.74·0.75), 여기를 한 번 통과시키자 0.19·0.21 로 떨어졌다.
        /// </summary>
        static void LowPass(float[] d, float k)
        {
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                lp += (d[i] - lp) * k;
                d[i] = lp;
            }
        }

        /// <summary>계산한 숫자 배열을 실제 AudioClip 으로 바꾼다.</summary>
        static AudioClip ToClip(string name, float[] data)
        {
            // 전체를 최대치 0.9 에 맞춰 키우거나 줄인다(정규화).
            //
            // 이걸 안 하면 어떤 소리는 계산 결과가 1.0 을 넘어 버린다. 스피커가 낼 수 있는
            // 한계가 1.0 이라 넘는 부분은 잘려 나가고, 그 잘린 모서리가 지직거리는
            // 잡음으로 들린다(클리핑). 실제로 동전 던지기 1.60, 환풍구 1.52 가 나왔다.
            //
            // 여기서 한 번에 맞춰 두면 소리별 음량은 gains 숫자 하나로만 조절하면 된다.
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak > 0.0001f)
            {
                float scale = 0.9f / peak;
                for (int i = 0; i < data.Length; i++) data[i] *= scale;
            }

            // 마지막 3ms 를 서서히 0으로 줄인다. 뚝 끊기면 "딱" 하는 잡음이 난다.
            int tail = Mathf.Min(data.Length, Rate / 300);
            for (int i = 0; i < tail; i++)
            {
                int k = data.Length - 1 - i;
                data[k] *= i / (float)tail;
            }

            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 한 주기 안에서 위치를 받아 파형 값(-1~1)을 돌려준다.
        ///
        ///   Sine    제일 부드럽다  — 배음이 없다. 둔탁한 소리, 저음
        ///   Tri     부드럽다      — 배음이 빠르게 줄어든다. 알림음·성공음
        ///   Square  딱딱하다      — 배음이 많다. 경보처럼 일부러 날을 세울 때만
        ///   Saw     제일 날카롭다 — 배음이 가장 많다. 기계음·하강음
        ///
        /// Square 와 Saw 는 그대로 쓰면 귀가 아프다. 반드시 LowPass 와 같이 쓴다.
        /// </summary>
        static float Wave(Shape shape, float phase)
        {
            float f = phase - Mathf.Floor(phase);   // 0~1 로 자른다
            switch (shape)
            {
                case Shape.Square: return f < 0.5f ? 1f : -1f;
                case Shape.Saw:    return f * 2f - 1f;
                case Shape.Tri:    return f < 0.5f ? (f * 4f - 1f) : (3f - f * 4f);
                default:           return Mathf.Sin(f * Mathf.PI * 2f);
            }
        }

        /// <summary>
        /// 발소리. 잡음을 저역 통과 필터에 통과시켜 "쉭" 을 "툭" 으로 바꾸고,
        /// 저음 한 방을 섞어 무게를 준다.
        ///
        /// cut 이 작을수록 먹먹해진다. 걸을 때 0.22, 길 때 0.08 을 쓴다.
        /// </summary>
        static float[] Step(float amp, float cut, float thumpHz)
        {
            int n = Len(0.14f);
            var d = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;   // 저역 통과 필터가 기억하는 값

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // 한 극점 저역 통과 필터. 새 값 쪽으로 조금씩만 따라간다.
                // 즉 빠른 변화(고음)는 깎이고 느린 변화(저음)만 남는다.
                lp += (noise - lp) * cut;

                float env = Mathf.Exp(-t * 36f);    // 소리가 줄어드는 모양
                float thump = Mathf.Sin(t * thumpHz * Mathf.PI * 2f) * Mathf.Exp(-t * 26f);

                d[i] = (lp * 1.8f + thump * 0.6f) * env * amp;
            }
            return d;
        }

        /// <summary>
        /// 바람 소리. 필터를 시간에 따라 열었다 닫으면 "후욱" 하고 지나가는 느낌이 난다.
        /// </summary>
        static float[] Whoosh(float seconds, float cutMin, float cutMax)
        {
            int n = Len(seconds);
            var d = new float[n];
            var rng = new System.Random(19);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // 0 에서 1 로 올랐다가 다시 0 으로 내려오는 값. 필터와 음량 둘 다에 쓴다.
                float bell = Mathf.Sin(u * Mathf.PI);

                lp += (noise - lp) * Mathf.Lerp(cutMin, cutMax, bell);
                d[i] = lp * bell * 1.9f;
            }
            return d;
        }

        /// <summary>
        /// 금속이 부딪히는 소리. 서로 딱 떨어지지 않는 주파수 셋을 겹치면
        /// "음정" 이 아니라 "쨍" 하는 금속음으로 들린다.
        /// </summary>
        static float[] Metal()
        {
            int n = Len(0.55f);
            var d = new float[n];
            var rng = new System.Random(31);
            float[] freq  = { 1730f, 2630f, 3910f };
            float[] decay = { 9f, 13f, 19f };
            float[] amp   = { 0.55f, 0.30f, 0.18f };

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                float s = 0f;
                for (int k = 0; k < freq.Length; k++)
                    s += Mathf.Sin(t * freq[k] * Mathf.PI * 2f) * Mathf.Exp(-t * decay[k]) * amp[k];

                // 맨 앞에 아주 짧은 잡음을 얹어 "부딪힌 순간" 을 만든다.
                float click = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 400f) * 0.5f;

                d[i] = s + click;
            }
            return d;
        }

        /// <summary>레버를 내리는 기계음. 짧은 잡음 + 낮은 사각파 한 방.</summary>
        static float[] Clunk()
        {
            int n = Len(0.26f);
            var d = new float[n];
            var rng = new System.Random(53);
            float lp = 0f, phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.25f;

                phase += 165f / Rate;
                float body = Wave(Shape.Square, phase) * Mathf.Exp(-t * 22f) * 0.28f;

                d[i] = (lp * Mathf.Exp(-t * 60f) * 1.4f) + body;
            }
            return d;
        }

        /// <summary>
        /// 음높이가 f0 에서 f1 로 미끄러지는 소리. 정전·붙잡힘처럼 쭉 변하는 소리에 쓴다.
        ///
        /// 주의: sin(2파이 · f(t) · t) 처럼 쓰면 안 된다. 그러면 실제로 들리는 음높이가
        /// 의도한 값과 달라진다. 매 샘플마다 "지금 주파수 나누기 샘플레이트" 만큼
        /// 위치를 밀어 주는 방식이 맞다.
        /// </summary>
        static float[] Sweep(float seconds, float f0, float f1,
                             Shape shape, float decay, float amp, float noiseMix)
        {
            int n = Len(seconds);
            var d = new float[n];
            var rng = new System.Random(71);
            float phase = 0f, lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = i / (float)n;

                float f = Mathf.Lerp(f0, f1, u);
                phase += f / Rate;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.09f;

                float env = Mathf.Exp(-t * decay);
                d[i] = (Wave(shape, phase) * (1f - noiseMix) + lp * noiseMix * 2f) * env * amp;
            }
            return d;
        }

        /// <summary>
        /// 음을 차례로 늘어놓는다. 올라가면 성공·획득으로, 내려가면 실패로 들린다.
        ///
        /// 음마다 앞뒤를 부드럽게 여닫지 않으면 음이 바뀌는 지점에서 "딱" 소리가 난다.
        /// </summary>
        static float[] Notes(float[] freqs, float noteSeconds, Shape shape, float amp)
        {
            int per = Len(noteSeconds);
            int n = per * freqs.Length;
            var d = new float[n];

            for (int k = 0; k < freqs.Length; k++)
            {
                float phase = 0f;

                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)Rate;
                    phase += freqs[k] / Rate;

                    // 앞 2ms 는 서서히 켜고, 그 뒤로는 서서히 끈다.
                    float attack = Mathf.Min(1f, t * 500f);
                    float env = attack * Mathf.Exp(-t * (2.2f / noteSeconds));

                    d[k * per + i] = Wave(shape, phase) * env * amp;
                }
            }
            return d;
        }
    }
}
