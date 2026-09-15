using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 인게임 HUD. IMGUI로 빠르게 올려서 시스템 동작을 눈으로 확인하는 용도다.
    /// 에셋 작업 단계에서 uGUI / UI Toolkit으로 교체할 예정.
    /// </summary>
    //
    // ── IMGUI 의 기본 ──
    // OnGUI() 는 유니티가 매 프레임(정확히는 프레임당 여러 번) 불러주는 그리기 함수다.
    // 여기서 GUI.Label(사각형, 글자, 스타일) 처럼 "이 자리에 이걸 그려라"를 나열한다.
    // 화면에 오브젝트가 남지 않고 매번 새로 그리므로, 조건에 따라 나타났다 사라지는 UI를
    // if 문 하나로 만들 수 있다.
    //
    // 좌표계 주의: IMGUI 의 y 는 화면 '위'가 0이고 아래로 갈수록 커진다.
    // 월드 좌표나 마우스 좌표(아래가 0)와 반대라, 변환할 때 Screen.height 에서 빼준다.
    public class StealthHUD : MonoBehaviour
    {
        [Header("Layout")]
        // 이 높이를 기준으로 UI 크기를 정하고, 실제 해상도에 맞춰 통째로 확대·축소한다.
        // 그래서 720p 든 4K 든 UI 비율이 똑같이 보인다.
        public int referenceHeight = 1080;

        // GUIStyle = 글꼴·크기·색·정렬을 담은 묶음. 매 프레임 새로 만들면 느려서 한 번만 만든다.
        GUIStyle labelStyle, titleStyle, centerStyle, bigStyle, hintStyle, buttonStyle, badgeLabelStyle;
        bool stylesReady;

        float scale = 1f;      // 화면 크기 대비 확대 배율
        float viewW, viewH;    // 배율을 적용한 뒤의 논리적 화면 크기
        Vector2 mouse;         // IMGUI 좌표계로 변환한 마우스 위치
        Rect retryButton, stageSelectButton;   // 결과 화면 버튼 영역. 클릭 판정에 쓴다

        StealthMenuUI menu;
        PlayerController player;
        Camera cam;

        static readonly Color KeyAccent = new Color(0.4f, 0.9f, 1f);     // 일반 상호작용 — 하늘색
        static readonly Color PrizeAccent = new Color(0.45f, 1f, 0.95f); // 고액 자료 — 청록

        void Awake() => menu = GetComponent<StealthMenuUI>();

        void Start()
        {
            player = FindFirstObjectByType<PlayerController>();
            cam = Camera.main;
        }

        // ------------------------------------------------------------ 입력

        /// <summary>
        /// 결과 화면의 버튼 클릭만 처리한다.
        ///
        /// IMGUI 의 GUI.Button 을 쓰지 않고 직접 판정하는 이유: 결과 화면에서는 timeScale 이 0이고
        /// 새 Input System 만 쓰는 구성이라, 구형 Input 에 의존하는 GUI.Button 이 동작하지 않는다.
        /// 그래서 사각형(Rect)과 마우스 좌표를 직접 비교한다.
        /// </summary>
        void Update()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null) return;
            if (gm.State != MissionState.Success && gm.State != MissionState.Failed) return;

            Layout();

            var m = Mouse.current;
            if (m == null || !m.leftButton.wasPressedThisFrame) return;

            // Rect.Contains(점) = 그 점이 사각형 안에 있는가. 클릭 판정의 전부다.
            if (retryButton.Contains(mouse)) gm.Restart();
            else if (stageSelectButton.Contains(mouse))
            {
                if (menu != null) menu.OpenStageSelect();
                else gm.OpenMenu();
            }
        }

        /// <summary>해상도에 맞춘 배율과 버튼 위치를 다시 계산한다.</summary>
        void Layout()
        {
            // 1080 기준으로 몇 배인가. 최소 0.75를 보장해 창을 아주 작게 줄여도 글씨가 안 뭉갠다.
            scale = Mathf.Max(0.75f, Screen.height / (float)referenceHeight);

            // 배율을 적용한 뒤의 좌표계 크기. 아래 모든 위치 계산은 이 값을 기준으로 한다.
            viewW = Screen.width / scale;
            viewH = Screen.height / scale;

            var m = Mouse.current;
            mouse = m != null
                // 마우스는 아래가 y=0, IMGUI 는 위가 y=0 이라 뒤집는다. 그리고 배율로 나눈다.
                ? new Vector2(m.position.ReadValue().x / scale, (Screen.height - m.position.ReadValue().y) / scale)
                // 마우스가 없으면 화면 밖 좌표를 넣어 어떤 버튼에도 안 걸리게 한다.
                : new Vector2(-9999f, -9999f);

            Rect panel = ResultPanelRect();
            retryButton = new Rect(panel.x + 42f, panel.yMax - 74f, 178f, 46f);
            stageSelectButton = new Rect(panel.xMax - 220f, panel.yMax - 74f, 178f, 46f);
        }

        Rect ResultPanelRect() => new Rect((viewW - 480f) * 0.5f, (viewH - 300f) * 0.5f, 480f, 300f);

        // ------------------------------------------------------------ 그리기

        /// <summary>
        /// OnGUI — 유니티가 UI를 그릴 때 불러준다. 여기 있는 것이 화면에 보이는 전부다.
        /// 그리는 순서가 곧 겹치는 순서다. 나중에 그린 것이 위에 올라간다.
        /// </summary>
        void OnGUI()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null || gm.State == MissionState.Menu) return;   // 메뉴는 StealthMenuUI 담당

            if (!stylesReady) BuildStyles();
            Layout();

            // GUI.matrix 에 확대 행렬을 넣으면 이후 그리는 모든 것이 통째로 확대된다.
            // 덕분에 아래 코드들은 해상도를 신경 쓰지 않고 1080 기준 좌표만 쓰면 된다.
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

            DrawMissionPanel(gm);
            DrawAwareness();
            DrawWorldPrompts();
            DrawControlHint();
            DrawRespawnNotice(gm);

            if (gm.State != MissionState.Playing) DrawResult(gm);

            // 암전은 무조건 마지막. HUD와 결과창까지 함께 덮어야 한다.
            DrawCaptureFade(gm);
        }

        /// <summary>
        /// 글자 스타일을 한 번만 만들어 둔다.
        /// Awake 가 아니라 첫 OnGUI 에서 만드는 이유: GUI.skin 은 OnGUI 안에서만 접근할 수 있다.
        /// </summary>
        void BuildStyles()
        {
            labelStyle = UIKit.Style(16, TextAnchor.MiddleLeft, new Color(0.92f, 0.94f, 0.97f));
            titleStyle = UIKit.Style(19, TextAnchor.MiddleLeft, new Color(0.92f, 0.94f, 0.97f), true);
            centerStyle = UIKit.Style(16, TextAnchor.MiddleCenter, new Color(0.92f, 0.94f, 0.97f));
            bigStyle = UIKit.Style(44, TextAnchor.MiddleCenter, Color.white, true);
            hintStyle = UIKit.Style(14, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.45f));
            buttonStyle = UIKit.Style(17, TextAnchor.MiddleCenter, new Color(0.95f, 0.97f, 1f), true);
            badgeLabelStyle = UIKit.Style(15, TextAnchor.MiddleLeft, new Color(0.93f, 0.95f, 0.98f));
            stylesReady = true;
        }

        /// <summary>좌측 상단 패널 — 스테이지 이름, 자료 수(또는 점수), 시간, 경보 단계.</summary>
        void DrawMissionPanel(StealthGameManager gm)
        {
            // Rect(x, y, 너비, 높이). 화면 왼쪽 위에서 24px 떨어진 자리.
            var rect = new Rect(24f, 24f, 310f, 128f);
            UIKit.Fill(rect, new Color(0.05f, 0.06f, 0.09f, 0.72f));   // 반투명 검은 배경
            // 왼쪽 가장자리에 4px 하늘색 띠. 이런 작은 장식이 UI를 정돈돼 보이게 한다.
            UIKit.Fill(new Rect(rect.x, rect.y, 4f, rect.height), new Color(0.35f, 0.85f, 1f, 0.9f));

            GUI.Label(new Rect(rect.x + 18f, rect.y + 12f, rect.width - 30f, 26f), gm.stageName, titleStyle);

            if (gm.endlessMode)
            {
                int high = EndlessMode.Instance != null ? EndlessMode.Instance.HighScore : 0;
                GUI.Label(new Rect(rect.x + 18f, rect.y + 42f, rect.width - 30f, 26f),
                    $"점수   {gm.Collected}",
                    UIKit.Style(20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.3f), true));
                GUI.Label(new Rect(rect.x + 18f, rect.y + 70f, rect.width - 30f, 24f),
                    $"최고   {high}", labelStyle);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 18f, rect.y + 44f, rect.width - 30f, 24f),
                    $"기밀 자료   {gm.Collected} / {gm.Required}", labelStyle);

                // 초를 분:초로 나눈다. FloorToInt 는 소수점을 버린 정수.
                int minutes = Mathf.FloorToInt(gm.ElapsedTime / 60f);
                int seconds = Mathf.FloorToInt(gm.ElapsedTime % 60f);   // % 는 나머지

                // $"..." 는 문자열 보간. { } 안의 값이 그 자리에 들어간다.
                // {minutes:00} 의 :00 은 서식 지정자로 "두 자리로, 없으면 0을 채워" 라는 뜻이다.
                // 그래서 7초가 "7"이 아니라 "07"로 나온다.
                GUI.Label(new Rect(rect.x + 18f, rect.y + 70f, rect.width - 30f, 24f),
                    $"경과 시간   {minutes:00}:{seconds:00}", labelStyle);
            }

            Color alertColor = gm.CaptureCount switch
            {
                0 => new Color(0.4f, 0.95f, 0.6f),
                1 => new Color(1f, 0.9f, 0.3f),
                2 => new Color(1f, 0.6f, 0.2f),
                _ => new Color(1f, 0.3f, 0.3f)
            };

            // 붙잡힌 횟수가 곧 경보 단계이자 남은 기회다. 점으로 한눈에 보이게 그린다.
            GUI.Label(new Rect(rect.x + 18f, rect.y + 96f, 110f, 24f), gm.endlessMode ? "목숨" : "경보 단계", labelStyle);
            for (int i = 0; i < gm.maxCaptures; i++)
            {
                var dot = new Rect(rect.x + 122f + i * 20f, rect.y + 103f, 13f, 13f);
                UIKit.Fill(dot, i < gm.CaptureCount ? alertColor : new Color(1f, 1f, 1f, 0.14f));
                UIKit.Outline(dot, new Color(1f, 1f, 1f, 0.22f));
            }
            GUI.Label(new Rect(rect.x + 128f + gm.maxCaptures * 20f, rect.y + 96f, 160f, 24f),
                $"{gm.CaptureCount} / {gm.maxCaptures}",
                UIKit.Style(15, TextAnchor.MiddleLeft, alertColor, true));
        }

        /// <summary>발각 순간의 암전. 이 뒤에서 플레이어가 시작 지점으로 옮겨진다.</summary>
        void DrawCaptureFade(StealthGameManager gm)
        {
            if (gm.CaptureFade <= 0f) return;

            UIKit.Fill(new Rect(0f, 0f, viewW, viewH), new Color(0f, 0f, 0f, gm.CaptureFade));

            // 완전히 어두워진 구간에서만 문구를 띄운다.
            if (gm.CaptureFade < 0.95f) return;

            GUI.Label(new Rect(0f, viewH * 0.5f - 40f, viewW, 44f), "발각",
                UIKit.Style(40, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.35f), true));
            GUI.Label(new Rect(0f, viewH * 0.5f + 8f, viewW, 28f),
                gm.endlessMode
                    ? $"남은 목숨 {gm.maxCaptures - gm.CaptureCount} / {gm.maxCaptures}"
                    : $"경보 {gm.CaptureCount} / {gm.maxCaptures}",
                UIKit.Style(18, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.88f)));
        }

        /// <summary>붙잡혀 시작 지점으로 돌아갔을 때 잠깐 뜨는 안내.</summary>
        void DrawRespawnNotice(StealthGameManager gm)
        {
            if (gm.RespawnNotice <= 0f) return;

            float alpha = Mathf.Clamp01(gm.RespawnNotice / 0.6f);
            const float boxW = 520f;
            var box = new Rect((viewW - boxW) * 0.5f, viewH * 0.30f, boxW, 88f);

            UIKit.Fill(box, new Color(0.10f, 0.04f, 0.05f, 0.88f * alpha));
            UIKit.Outline(box, new Color(1f, 0.4f, 0.4f, 0.8f * alpha));

            GUI.Label(new Rect(box.x, box.y + 14f, box.width, 32f), "발각됐다 — 시작 지점으로 복귀",
                UIKit.Style(24, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.55f, alpha), true));
            GUI.Label(new Rect(box.x, box.y + 50f, box.width, 24f),
                gm.endlessMode
                    ? $"회수한 자료는 유지된다   ·   남은 목숨 {gm.maxCaptures - gm.CaptureCount}   ·   경비가 더 예민해졌다"
                    : $"회수한 자료는 유지된다   ·   경보 {gm.CaptureCount} / {gm.maxCaptures}   ·   경비가 더 예민해졌다",
                UIKit.Style(15, TextAnchor.MiddleCenter, new Color(0.9f, 0.85f, 0.85f, alpha)));
        }

        /// <summary>
        /// 화면 아래 중앙의 경계 게이지.
        /// 경비가 여럿이므로 "가장 높은 경계도"와 "가장 위험한 상태"를 찾아 그것만 보여준다.
        /// 전부 보여주면 정보가 너무 많아 읽히지 않는다.
        /// </summary>
        void DrawAwareness()
        {
            float maxAwareness = 0f;
            GuardState worst = GuardState.Patrol;

            foreach (var guard in GuardAI.All)
            {
                if (guard == null) continue;
                if (guard.Awareness > maxAwareness) maxAwareness = guard.Awareness;

                // 상태는 크기를 비교할 수 없으므로 Priority() 로 숫자를 매겨 비교한다.
                if (Priority(guard.State) > Priority(worst)) worst = guard.State;
            }

            // 드론은 경비와 별개로 경계도를 올린다. 둘 중 높은 쪽을 보여준다.
            maxAwareness = Mathf.Max(maxAwareness, SurveillanceDrone.MaxAwareness);

            bool inHideSpot = player != null && player.IsHidden;
            bool underCover = player != null && player.IsUnderCover;
            bool hidden = inHideSpot || underCover;

            string label = hidden
                ? (inHideSpot ? "숨음 — 시야에서 완전히 벗어남" : "통로 아래 — 경비에게 보이지 않는다")
                : worst switch
                {
                    GuardState.Chase => "발각!",
                    GuardState.Search => "수색 중",
                    GuardState.Suspicious => "의심 받는 중",
                    GuardState.Return => "복귀 중",
                    _ => maxAwareness > 0.05f ? "주의" : "은신 중"
                };

            Color barColor = hidden
                ? new Color(0.3f, 0.85f, 1f)
                : worst == GuardState.Chase
                    ? new Color(1f, 0.25f, 0.25f)
                    : Color.Lerp(new Color(0.35f, 1f, 0.6f), new Color(1f, 0.75f, 0.15f), maxAwareness);

            const float barW = 380f;
            const float barH = 12f;

            // (화면너비 - 바너비) / 2 = 가운데 정렬. UI 에서 가장 많이 쓰는 계산이다.
            var barRect = new Rect((viewW - barW) * 0.5f, viewH - 108f, barW, barH);

            GUI.Label(new Rect(barRect.x, barRect.y - 30f, barW, 24f), label, centerStyle);
            UIKit.Fill(barRect, new Color(0.05f, 0.06f, 0.09f, 0.8f));   // 빈 바(배경)

            // 채워진 부분. 너비에 0~1 값을 곱하는 것이 게이지 바의 전부다.
            UIKit.Fill(new Rect(barRect.x, barRect.y, barW * maxAwareness, barH), barColor);

            UIKit.Outline(barRect, new Color(1f, 1f, 1f, 0.15f));        // 테두리
        }

        /// <summary>
        /// 상호작용할 수 있는 대상 위에 작은 키 배지를 띄운다.
        /// 화면 하단 한 곳에 몰아서 띄우면 "무엇에 대한 안내인지"가 안 보이기 때문에
        /// 대상의 머리 위, 즉 월드 좌표를 화면으로 투영한 자리에 붙인다.
        /// </summary>
        void DrawWorldPrompts()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            foreach (var objective in Objective.All)
            {
                if (objective == null) continue;

                // 쿨다운 중인 고액 자료는 바닥 원 한가운데에 남은 초를 띄운다.
                // 언제 다시 생기는지 보여야 "지금 중앙으로 갈지"를 판단할 수 있다.
                if (objective.Collected)
                {
                    if (objective.respawnDelay <= 0f || objective.RespawnRemaining <= 0f) continue;
                    DrawWorldNumber(objective.HomePosition + Vector3.up * 0.2f,
                        Mathf.CeilToInt(objective.RespawnRemaining).ToString(), PrizeAccent);
                    continue;
                }

                if (!objective.PlayerInRange) continue;
                DrawKeyBadge(objective.transform.position + Vector3.up * 1.5f,
                    "E", $"{objective.displayName} 회수 (길게)", objective.HoldProgress,
                    objective.points > 1 ? PrizeAccent : KeyAccent);
            }

            foreach (var lever in PowerLever.All)
            {
                if (lever == null || !lever.PlayerInRange) continue;

                if (lever.Ready)
                {
                    DrawKeyBadge(lever.transform.position + Vector3.up * 1.7f,
                        "F", lever.displayName + " 내리기", -1f, new Color(1f, 0.85f, 0.3f));
                }
                else
                {
                    DrawKeyBadge(lever.transform.position + Vector3.up * 1.7f,
                        "", $"재충전 {lever.CooldownRemaining:0.0}초", -1f, new Color(0.55f, 0.57f, 0.62f));
                }
            }

            if (player != null && player.IsHidden)
            {
                DrawKeyBadge(player.transform.position + Vector3.up * 1.3f,
                    "H", "나오기", -1f, new Color(0.3f, 1f, 0.65f));
            }
            else if (player != null && player.NearbyHideSpot != null)
            {
                DrawKeyBadge(player.NearbyHideSpot.transform.position + Vector3.up * 1.3f,
                    "H", "숨기", -1f, new Color(0.3f, 1f, 0.65f));
            }

            foreach (var vent in Vent.All)
            {
                if (vent == null || !vent.PlayerInRange) continue;
                DrawKeyBadge(vent.transform.position + Vector3.up * 1.2f,
                    "V", vent.displayName + " 타기", -1f, new Color(0.45f, 0.9f, 1f));
            }

            var exit = ExitZone.Instance;
            if (exit == null || !exit.PlayerInside) return;

            if (exit.Unlocked)
            {
                DrawKeyBadge(exit.transform.position + Vector3.up * 1.4f,
                    "", "탈출 중", exit.ExtractProgress, new Color(0.35f, 1f, 0.6f));
            }
            else
            {
                DrawKeyBadge(exit.transform.position + Vector3.up * 1.4f,
                    "", "자료를 모두 회수해야 한다", -1f, new Color(1f, 0.55f, 0.45f));
            }
        }

        /// <summary>월드 좌표 위에 뜨는 키 배지. key 가 비어 있으면 안내 문구만 그린다.</summary>
        /// <summary>
        /// 월드 좌표 위에 숫자만 띄운다. 상자를 씌우면 바닥에 깔린 쿨다운 원을 가리므로
        /// 배경 없이 그리고, 대신 어두운 그림자를 한 겹 깔아 어느 바닥색 위에서도 읽히게 한다.
        /// </summary>
        void DrawWorldNumber(Vector3 worldPosition, string text, Color color)
        {
            Vector3 screen = cam.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;

            float x = screen.x / scale;
            float y = (Screen.height - screen.y) / scale;
            var rect = new Rect(x - 44f, y - 22f, 88f, 44f);

            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text,
                UIKit.Style(30, TextAnchor.MiddleCenter, new Color(0f, 0f, 0f, 0.7f), true));
            GUI.Label(rect, text, UIKit.Style(30, TextAnchor.MiddleCenter, color, true));
        }

        /// <summary>
        /// 월드 좌표 위에 뜨는 키 배지. key 가 비어 있으면 안내 문구만 그린다.
        /// progress 가 0 이상이면 아래에 진행 바가 붙는다(-1이면 없음).
        /// </summary>
        void DrawKeyBadge(Vector3 worldPosition, string key, string label, float progress, Color accent)
        {
            // WorldToScreenPoint = 3D 월드 좌표 → 화면 픽셀 좌표.
            // 월드에 있는 물건 위에 UI를 띄우는 핵심 함수다.
            Vector3 screen = cam.WorldToScreenPoint(worldPosition);

            // z 는 카메라로부터의 깊이. 음수면 카메라 뒤라서 화면에 그리면 안 된다.
            // (안 막으면 뒤에 있는 것이 화면에 뒤집혀 나타난다)
            if (screen.z <= 0f) return;

            float x = screen.x / scale;
            float y = (Screen.height - screen.y) / scale;   // y 뒤집기

            // CalcSize = 이 글자가 이 스타일로 그려지면 몇 픽셀인가.
            // 글자 길이에 맞춰 상자 크기를 정할 때 쓴다.
            Vector2 labelSize = badgeLabelStyle.CalcSize(new GUIContent(label));

            const float keySize = 24f;
            const float pad = 9f;
            float gap = string.IsNullOrEmpty(key) ? 0f : keySize + 7f;   // 키 칸이 없으면 자리도 없다
            float boxW = pad + gap + labelSize.x + pad;
            float boxH = progress >= 0f ? 42f : 32f;                     // 진행 바가 있으면 더 높게

            var box = new Rect(x - boxW * 0.5f, y - boxH, boxW, boxH);

            UIKit.Fill(box, new Color(0.04f, 0.05f, 0.07f, 0.92f));
            UIKit.Outline(box, new Color(accent.r, accent.g, accent.b, 0.55f));

            float textX = box.x + pad;

            if (!string.IsNullOrEmpty(key))
            {
                var keyRect = new Rect(box.x + pad, box.y + 4f, keySize, keySize);
                UIKit.Fill(keyRect, new Color(accent.r, accent.g, accent.b, 0.22f));
                UIKit.Outline(keyRect, new Color(accent.r, accent.g, accent.b, 0.9f));
                GUI.Label(keyRect, key, UIKit.Style(15, TextAnchor.MiddleCenter, accent, true));
                textX += gap;
            }

            GUI.Label(new Rect(textX, box.y + 4f, labelSize.x + 4f, keySize), label, badgeLabelStyle);

            if (progress < 0f) return;

            var fill = new Rect(box.x + pad, box.yMax - 11f, boxW - pad * 2f, 5f);
            UIKit.Fill(fill, new Color(1f, 1f, 1f, 0.14f));
            UIKit.Fill(new Rect(fill.x, fill.y, fill.width * Mathf.Clamp01(progress), fill.height), accent);
        }

        void DrawControlHint()
        {
            GUI.Label(new Rect(viewW - 360f, viewH - 44f, 340f, 24f),
                "WASD 이동   C 기어가기   H 숨기   Q 동전   F 두꺼비집   E 회수", hintStyle);
        }

        void DrawResult(StealthGameManager gm)
        {
            UIKit.Fill(new Rect(0f, 0f, viewW, viewH), new Color(0.02f, 0.02f, 0.04f, 0.78f));

            bool success = gm.State == MissionState.Success;
            Rect panel = ResultPanelRect();
            Color accent = success ? new Color(0.35f, 1f, 0.6f) : new Color(1f, 0.38f, 0.38f);

            UIKit.Fill(panel, new Color(0.06f, 0.07f, 0.10f, 0.97f));
            UIKit.Outline(panel, new Color(accent.r, accent.g, accent.b, 0.7f));

            string headline = gm.endlessMode ? "RUN OVER" : (success ? "MISSION COMPLETE" : "CAPTURED");
            GUI.Label(new Rect(panel.x, panel.y + 32f, panel.width, 56f), headline,
                UIKit.Style(42, TextAnchor.MiddleCenter, accent, true));

            int minutes = Mathf.FloorToInt(gm.ElapsedTime / 60f);
            int seconds = Mathf.FloorToInt(gm.ElapsedTime % 60f);

            if (gm.endlessMode)
            {
                var endless = EndlessMode.Instance;
                int high = endless != null ? endless.HighScore : 0;
                bool record = endless != null && endless.NewRecord;

                GUI.Label(new Rect(panel.x, panel.y + 100f, panel.width, 40f), $"{gm.Collected}",
                    UIKit.Style(46, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), true));
                GUI.Label(new Rect(panel.x, panel.y + 142f, panel.width, 24f), "회수한 자료", centerStyle);

                GUI.Label(new Rect(panel.x, panel.y + 176f, panel.width, 26f),
                    record ? "신기록!" : $"최고 기록   {high}",
                    UIKit.Style(18, TextAnchor.MiddleCenter,
                        record ? new Color(1f, 0.85f, 0.3f) : new Color(0.75f, 0.79f, 0.85f), record));
            }
            else if (success)
            {
                GUI.Label(new Rect(panel.x, panel.y + 104f, panel.width, 32f), $"랭크   {gm.EvaluateRank()}",
                    UIKit.Style(28, TextAnchor.MiddleCenter, new Color(0.95f, 0.97f, 1f), true));
                GUI.Label(new Rect(panel.x, panel.y + 146f, panel.width, 24f),
                    $"소요 시간 {minutes:00}:{seconds:00}     발각 {gm.DetectionCount}회", centerStyle);
            }
            else
            {
                GUI.Label(new Rect(panel.x, panel.y + 112f, panel.width, 24f), "경비에게 붙잡혔다", centerStyle);
                GUI.Label(new Rect(panel.x, panel.y + 146f, panel.width, 24f),
                    $"생존 시간 {minutes:00}:{seconds:00}     회수 {gm.Collected}/{gm.Required}", centerStyle);
            }

            DrawButton(retryButton, "다시 시도 (R)", accent);
            DrawButton(stageSelectButton, "스테이지 선택", new Color(0.6f, 0.68f, 0.78f));
        }

        /// <summary>버튼 하나를 그린다. 클릭 처리는 Update 에서 따로 한다.</summary>
        void DrawButton(Rect rect, string label, Color accent)
        {
            // 마우스가 위에 있으면 밝게. 누를 수 있다는 신호다.
            bool hover = rect.Contains(mouse);

            UIKit.Fill(rect, hover
                ? new Color(accent.r, accent.g, accent.b, 0.22f)
                : new Color(0.09f, 0.11f, 0.14f, 0.95f));
            UIKit.Outline(rect, new Color(accent.r, accent.g, accent.b, hover ? 1f : 0.45f));
            GUI.Label(rect, label, buttonStyle);
        }

        /// <summary>상태의 위험도 순위. 숫자가 클수록 위험하다. 가장 위험한 상태를 고르는 데 쓴다.</summary>
        static int Priority(GuardState state) => state switch
        {
            GuardState.Chase => 4,
            GuardState.Search => 3,
            GuardState.Suspicious => 2,
            GuardState.Return => 1,
            _ => 0
        };
    }
}
