using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth
{
    /// <summary>
    /// 타이틀 / 스테이지 선택 / 조작키 화면.
    ///
    /// 클릭 판정은 IMGUI 버튼이 아니라 Input System 마우스로 직접 처리한다.
    /// 이 프로젝트는 새 Input System만 쓰기 때문에 OnGUI 버튼 입력을 신뢰하기 어렵고,
    /// timeScale 이 0인 상태에서도 확실히 동작해야 하기 때문이다.
    /// </summary>
    public class StealthMenuUI : MonoBehaviour
    {
        // 메뉴도 상태머신이다. 지금 어느 화면인지를 변수 하나로 들고,
        // Update 와 OnGUI 에서 그 값에 따라 갈라진다. 경비 AI 와 완전히 같은 구조다.
        //
        // enum 을 클래스 안에 선언하면 이 클래스에서만 쓰는 값이라는 뜻이 된다.
        // 밖에서 쓸 일이 없으므로 파일을 따로 만들지 않았다.
        enum MenuScreen { Title, StageSelect, Controls, Paused }

        [Header("Branding")]
        public string gameTitle = "SHADOW PROTOCOL";
        public string gameSubtitle = "잠입 · 회수 · 탈출";

        [Header("Layout")]
        public int referenceHeight = 1080;

        // ── 화면에 표시할 안내 문구들 ──
        //
        // (string key, string action) 는 튜플(tuple)이다. 클래스나 struct 를 따로 만들지 않고
        // 값 두 개를 묶는 가벼운 방법. 이름을 붙여두면 row.key / row.action 으로 꺼낼 수 있다.
        //
        // 문구를 코드 중간이 아니라 여기 배열로 모아둔 이유는, 내용을 고칠 때
        // 그리는 코드를 건드리지 않아도 되고 줄을 추가·삭제하기도 쉽기 때문이다.
        static readonly (string key, string action)[] ControlRows =
        {
            ("W A S D", "이동"),
            ("C", "낮게 기기 — 크롤 통로를 지나고, 그 아래에서는 안 보인다"),
            ("Q", "동전 던지기 — 커서 위치로 던져 경비를 유인"),
            ("F", "두꺼비집 — 10초간 정전, 완전히 안 보인다"),
            ("H", "오크통 더미에 숨기 / 나오기"),
            ("V", "환풍구 타기 — 짝이 되는 환풍구로 이동"),
            ("E (길게)", "기밀 자료 회수"),
            ("ESC", "일시정지 — 계속하기 / 스테이지 선택"),
            ("F5", "즉시 재시작"),
        };

        static readonly (string label, string detail)[] TipRows =
        {
            ("오크통·상자 더미", "곁에서 H를 누르면 안으로 숨는다. 시야에서 완전히 사라진다"),
            ("파란 바닥 통로", "크롤 통로. C로 기어들어가 있는 동안은 경비에게 보이지 않는다"),
            ("정전 중", "맵이 깜깜해지고 내 주변만 보인다. 그동안 경비에게 안 들킨다"),
            ("파란 원이 도는 복도", "감시 드론. 벽 뒤에 숨어도 소용없고 머리 위를 막아야 피한다"),
            ("발각되면", "즉시 시작 지점으로 복귀. 자료는 유지되지만 경비가 예민해진다"),
        };

        static readonly (Color color, string meaning)[] ConeLegend =
        {
            (new Color(0.35f, 1f, 0.6f), "순찰 — 아직 눈치채지 못함"),
            (new Color(1f, 0.9f, 0.25f), "의심 — 멈춰서 확인 중"),
            (new Color(1f, 0.6f, 0.15f), "수색 — 소리가 난 곳을 뒤짐"),
            (new Color(1f, 0.3f, 0.3f), "발각 — 즉시 붙잡힌다"),
        };

        MenuScreen screen = MenuScreen.Title;          // 지금 보고 있는 화면
        MenuScreen controlsReturnTo = MenuScreen.Title; // 조작법 화면에서 뒤로 가면 돌아갈 곳

        float scale = 1f;
        float viewW, viewH;
        Vector2 mouse;

        // 버튼 영역들. Layout() 에서 위치를 계산하고, Update() 에서 클릭 판정에,
        // OnGUI() 에서 그리기에 같은 값을 쓴다. 그래서 그림과 판정이 절대 어긋나지 않는다.
        Rect startButton;
        Rect resumeButton;
        Rect stageSelectButton;
        Rect controlsButton;
        Rect backButton;
        Rect closeButton;
        Rect unlockButton;
        Rect[] stageCards = new Rect[0];

        GUIStyle titleStyle, subtitleStyle, buttonStyle, headingStyle, bodyStyle, mutedStyle, keyStyle;
        bool stylesReady;

        /// <summary>결과 화면에서 "스테이지 선택"을 눌렀을 때 호출된다.</summary>
        public void OpenStageSelect()
        {
            screen = MenuScreen.StageSelect;
            StealthGameManager.Instance?.OpenMenu();
        }

        // ------------------------------------------------------------ 입력

        /// <summary>
        /// 키보드(ESC)와 마우스 클릭을 받는다. 그리기는 OnGUI 에서 따로 한다.
        /// 입력과 그리기를 나눠두면, 프레임당 여러 번 호출되는 OnGUI 에서
        /// 클릭이 중복 처리되는 사고를 피할 수 있다.
        /// </summary>
        void Update()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null) return;

            var kb = Keyboard.current;

            // 플레이 중 ESC = 일시정지. 메뉴와 같은 화면을 쓰지만 진행 상황은 그대로 둔다.
            // 발각 연출 중에는 무시한다. 그 사이에 멈추면 복귀 처리가 꼬인다.
            if (gm.State == MissionState.Playing)
            {
                if (!gm.CaptureSequenceActive && kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    screen = MenuScreen.Paused;
                    gm.OpenMenu();
                }
                return;
            }

            if (gm.State != MissionState.Menu) return;

            Layout();

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == MenuScreen.Controls) screen = controlsReturnTo;
                else if (screen == MenuScreen.Paused) gm.Resume();
                else if (screen == MenuScreen.StageSelect) screen = MenuScreen.Title;
                return;
            }

            var m = Mouse.current;
            if (m == null || !m.leftButton.wasPressedThisFrame) return;

            // 메뉴에서 왼쪽 버튼을 누르면 짧게 딸깍 소리를 낸다.
            // 버튼을 정확히 맞혔는지까지 보지는 않는다. 버튼마다 소리를 넣으려면
            // 아래 분기 여덟 군데에 같은 줄을 복사해야 하는데, 빈 곳을 눌렀을 때
            // 소리가 나는 것은 아무도 문제 삼지 않을 정도라 한 곳에서 끝냈다.
            GameAudio.Play(Sfx.UiClick);

            switch (screen)
            {
                case MenuScreen.Title:
                    if (startButton.Contains(mouse)) screen = MenuScreen.StageSelect;
                    else if (controlsButton.Contains(mouse))
                    {
                        controlsReturnTo = MenuScreen.Title;
                        screen = MenuScreen.Controls;
                    }
                    break;

                case MenuScreen.StageSelect:
                    for (int i = 0; i < stageCards.Length; i++)
                    {
                        if (!stageCards[i].Contains(mouse)) continue;
                        if (!StageCatalog.IsSelectable(StageCatalog.Stages[i])) continue;
                        gm.LoadStage(StageCatalog.Stages[i]);
                        return;
                    }
                    if (unlockButton.Contains(mouse))
                    {
                        StageProgress.UnlockAll = !StageProgress.UnlockAll;
                    }
                    else if (controlsButton.Contains(mouse))
                    {
                        controlsReturnTo = MenuScreen.StageSelect;
                        screen = MenuScreen.Controls;
                    }
                    else if (backButton.Contains(mouse)) screen = MenuScreen.Title;
                    break;

                case MenuScreen.Controls:
                    if (closeButton.Contains(mouse)) screen = controlsReturnTo;
                    break;

                case MenuScreen.Paused:
                    if (resumeButton.Contains(mouse)) gm.Resume();
                    else if (stageSelectButton.Contains(mouse)) screen = MenuScreen.StageSelect;
                    else if (controlsButton.Contains(mouse))
                    {
                        controlsReturnTo = MenuScreen.Paused;
                        screen = MenuScreen.Controls;
                    }
                    break;
            }
        }

        /// <summary>
        /// 화면 크기에 맞춰 모든 버튼의 위치를 다시 계산한다.
        /// 위치를 상수로 박아두지 않고 매번 계산하는 이유는, 창 크기를 바꿔도
        /// 버튼이 항상 가운데에 오게 하기 위해서다.
        /// </summary>
        void Layout()
        {
            // UnityEngine.Screen 이라고 전체 이름을 쓴 이유: 이 클래스 안에 MenuScreen 이라는
            // 타입이 있어서 그냥 Screen 이라고 쓰면 컴파일러가 헷갈릴 수 있다.
            scale = Mathf.Max(0.75f, UnityEngine.Screen.height / (float)referenceHeight);
            viewW = UnityEngine.Screen.width / scale;
            viewH = UnityEngine.Screen.height / scale;

            var m = Mouse.current;
            if (m != null)
            {
                Vector2 p = m.position.ReadValue();
                mouse = new Vector2(p.x / scale, (UnityEngine.Screen.height - p.y) / scale);
            }
            else
            {
                mouse = new Vector2(-9999f, -9999f);
            }

            startButton = new Rect((viewW - 280f) * 0.5f, viewH * 0.5f + 30f, 280f, 58f);
            resumeButton = new Rect((viewW - 300f) * 0.5f, viewH * 0.5f - 30f, 300f, 58f);
            stageSelectButton = new Rect((viewW - 300f) * 0.5f, viewH * 0.5f + 40f, 300f, 58f);
            controlsButton = new Rect(40f, viewH - 96f, 170f, 50f);
            backButton = new Rect((viewW - 200f) * 0.5f, viewH * 0.5f + 210f, 200f, 48f);

            // ── 스테이지 카드 목록을 세로로 쌓는다 ──
            int count = StageCatalog.Stages.Length;

            // 개수가 바뀌었을 때만 배열을 다시 만든다. 매 프레임 new 하면 쓰레기가 쌓인다.
            if (stageCards.Length != count) stageCards = new Rect[count];

            const float cardW = 600f;
            const float cardH = 78f;
            const float gap = 9f;

            // 전체 높이 = 카드 높이×개수 + 간격×(개수-1).
            // (cardH + gap) * count 에서 마지막 간격 하나를 빼는 식으로 계산했다.
            float totalH = count * (cardH + gap) - gap;

            // 화면 중앙에서 전체 높이의 절반만큼 위로 올리면 세로 가운데 정렬이 된다.
            float top = viewH * 0.5f - totalH * 0.5f + 16f;

            for (int i = 0; i < count; i++)
                stageCards[i] = new Rect((viewW - cardW) * 0.5f, top + i * (cardH + gap), cardW, cardH);

            backButton = new Rect((viewW - 200f) * 0.5f, top + totalH + 18f, 200f, 44f);
            unlockButton = new Rect((viewW + cardW) * 0.5f - 130f, top - 42f, 130f, 30f);

            var panel = ControlsPanelRect();
            closeButton = new Rect(panel.x + (panel.width - 180f) * 0.5f, panel.yMax - 68f, 180f, 46f);
        }

        Rect ControlsPanelRect()
        {
            const float pw = 720f;
            const float ph = 620f;
            return new Rect((viewW - pw) * 0.5f, (viewH - ph) * 0.5f, pw, ph);
        }

        // ------------------------------------------------------------ 그리기

        void OnGUI()
        {
            var gm = StealthGameManager.Instance;
            if (gm == null || gm.State != MissionState.Menu) return;

            if (!stylesReady) BuildStyles();
            Layout();

            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

            UIKit.Fill(new Rect(0f, 0f, viewW, viewH), new Color(0.02f, 0.025f, 0.04f, 0.88f));

            switch (screen)
            {
                case MenuScreen.Title: DrawTitle(); break;
                case MenuScreen.StageSelect: DrawStageSelect(); break;
                case MenuScreen.Controls: DrawControls(); break;
                case MenuScreen.Paused: DrawPaused(); break;
            }
        }

        void BuildStyles()
        {
            titleStyle = UIKit.Style(62, TextAnchor.MiddleCenter, new Color(0.94f, 0.96f, 1f), true);
            subtitleStyle = UIKit.Style(20, TextAnchor.MiddleCenter, new Color(0.55f, 0.72f, 0.85f));
            buttonStyle = UIKit.Style(21, TextAnchor.MiddleCenter, new Color(0.95f, 0.97f, 1f), true);
            headingStyle = UIKit.Style(30, TextAnchor.MiddleCenter, new Color(0.94f, 0.96f, 1f), true);
            bodyStyle = UIKit.Style(17, TextAnchor.MiddleLeft, new Color(0.88f, 0.91f, 0.95f));
            mutedStyle = UIKit.Style(15, TextAnchor.MiddleLeft, new Color(0.60f, 0.65f, 0.72f));
            keyStyle = UIKit.Style(17, TextAnchor.MiddleLeft, new Color(0.40f, 0.85f, 1f), true);
            stylesReady = true;
        }

        void DrawTitle()
        {
            GUI.Label(new Rect(0f, viewH * 0.5f - 190f, viewW, 80f), gameTitle, titleStyle);
            GUI.Label(new Rect(0f, viewH * 0.5f - 120f, viewW, 30f), gameSubtitle, subtitleStyle);

            UIKit.Fill(new Rect(viewW * 0.5f - 40f, viewH * 0.5f - 72f, 80f, 2f),
                new Color(0.35f, 0.85f, 1f, 0.6f));

            DrawButton(startButton, "게임 시작", true, new Color(0.35f, 0.85f, 1f));
            DrawButton(controlsButton, "조작키", true, new Color(0.6f, 0.68f, 0.78f));
        }

        /// <summary>플레이 중 ESC. 진행 상황을 그대로 보여주고 계속하기 / 스테이지 선택만 고르게 한다.</summary>
        void DrawPaused()
        {
            var gm = StealthGameManager.Instance;

            GUI.Label(new Rect(0f, viewH * 0.5f - 200f, viewW, 80f), "일시정지", titleStyle);

            if (gm != null)
            {
                string status = gm.endlessMode
                    ? $"{gm.stageName}   ·   회수 {gm.Collected}   ·   남은 목숨 {gm.maxCaptures - gm.CaptureCount}"
                    : $"{gm.stageName}   ·   자료 {gm.Collected}/{gm.Required}   ·   발각 {gm.CaptureCount}/{gm.maxCaptures}";
                GUI.Label(new Rect(0f, viewH * 0.5f - 132f, viewW, 30f), status, subtitleStyle);
            }

            UIKit.Fill(new Rect(viewW * 0.5f - 40f, viewH * 0.5f - 88f, 80f, 2f),
                new Color(0.35f, 0.85f, 1f, 0.6f));

            DrawButton(resumeButton, "계속하기   (ESC)", true, new Color(0.35f, 0.85f, 1f));
            DrawButton(stageSelectButton, "스테이지 선택으로", true, new Color(0.6f, 0.68f, 0.78f));
            DrawButton(controlsButton, "조작키", true, new Color(0.6f, 0.68f, 0.78f));
        }

        void DrawStageSelect()
        {
            if (stageCards.Length == 0) return;

            GUI.Label(new Rect(0f, stageCards[0].y - 62f, viewW, 40f), "스테이지 선택", headingStyle);

            for (int i = 0; i < stageCards.Length; i++)
            {
                DrawStageCard(StageCatalog.Stages[i], stageCards[i]);
            }

            DrawButton(unlockButton, StageProgress.UnlockAll ? "해금 해제" : "전체 해금", true,
                StageProgress.UnlockAll ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 0.55f, 0.62f));
            DrawButton(backButton, "뒤로", true, new Color(0.6f, 0.68f, 0.78f));
            DrawButton(controlsButton, "조작키", true, new Color(0.6f, 0.68f, 0.78f));
        }

        /// <summary>
        /// 스테이지 카드 한 장. 고를 수 있는지·클리어했는지에 따라 색이 달라진다.
        ///
        /// 먼저 상태를 bool 변수 네 개로 뽑아낸 다음 그리는 순서를 지킨다.
        /// 그리는 코드 안에 조건이 섞이면 어느 경우에 어떻게 보이는지 읽기 어려워진다.
        /// </summary>
        void DrawStageCard(StageEntry stage, Rect card)
        {
            bool selectable = StageCatalog.IsSelectable(stage);
            bool locked = stage.built && !selectable;      // 만들어졌지만 아직 안 열린 것 = 무한모드
            bool hover = selectable && card.Contains(mouse);
            bool cleared = StageProgress.IsCleared(stage.sceneName);

            Color accent = stage.isEndless ? new Color(1f, 0.78f, 0.3f) : new Color(0.35f, 0.85f, 1f);

            Color bg = selectable
                ? (hover ? new Color(0.13f, 0.20f, 0.26f, 0.98f) : new Color(0.07f, 0.09f, 0.12f, 0.95f))
                : new Color(0.05f, 0.055f, 0.07f, 0.9f);

            UIKit.Fill(card, bg);
            UIKit.Outline(card, selectable
                ? (hover ? new Color(accent.r, accent.g, accent.b, 0.9f) : new Color(1f, 1f, 1f, 0.14f))
                : new Color(1f, 1f, 1f, 0.06f));

            UIKit.Fill(new Rect(card.x, card.y, 4f, card.height), selectable
                ? new Color(accent.r, accent.g, accent.b, hover ? 1f : 0.65f)
                : new Color(0.4f, 0.42f, 0.46f, 0.5f));

            Color codeColor = selectable ? accent : new Color(0.45f, 0.47f, 0.52f);
            Color titleColor = selectable ? new Color(0.95f, 0.97f, 1f) : new Color(0.52f, 0.54f, 0.60f);
            Color featureColor = selectable ? new Color(0.62f, 0.70f, 0.80f) : new Color(0.40f, 0.42f, 0.47f);

            GUI.Label(new Rect(card.x + 22f, card.y + 10f, 150f, 20f), stage.code,
                UIKit.Style(14, TextAnchor.MiddleLeft, codeColor, true));
            GUI.Label(new Rect(card.x + 22f, card.y + 28f, card.width - 230f, 28f), stage.title,
                UIKit.Style(23, TextAnchor.MiddleLeft, titleColor, true));
            GUI.Label(new Rect(card.x + 22f, card.y + 54f, card.width - 200f, 20f),
                "+ " + stage.newFeature, UIKit.Style(14, TextAnchor.MiddleLeft, featureColor));

            // 오른쪽 상태 표시
            var status = new Rect(card.xMax - 190f, card.y + 26f, 168f, 30f);

            if (!stage.built)
            {
                GUI.Label(status, "준비 중",
                    UIKit.Style(16, TextAnchor.MiddleRight, new Color(0.50f, 0.52f, 0.58f), true));
            }
            else if (locked)
            {
                GUI.Label(status, "잠김 — 스테이지 5개 클리어 필요",
                    UIKit.Style(14, TextAnchor.MiddleRight, new Color(0.55f, 0.57f, 0.63f)));
            }
            else if (stage.isEndless)
            {
                GUI.Label(status, $"최고 기록  {StageProgress.EndlessHighScore}",
                    UIKit.Style(16, TextAnchor.MiddleRight, accent, true));
            }
            else if (cleared)
            {
                string rank = StageProgress.GetRank(stage.sceneName);
                GUI.Label(status, string.IsNullOrEmpty(rank) ? "클리어" : $"클리어   랭크 {rank}",
                    UIKit.Style(16, TextAnchor.MiddleRight, new Color(0.4f, 0.95f, 0.65f), true));
            }
            else if (hover)
            {
                GUI.Label(status, "시작 ▶", UIKit.Style(17, TextAnchor.MiddleRight, accent, true));
            }
        }

        void DrawControls()
        {
            Rect panel = ControlsPanelRect();
            UIKit.Fill(panel, new Color(0.06f, 0.07f, 0.10f, 0.98f));
            UIKit.Outline(panel, new Color(0.35f, 0.85f, 1f, 0.55f));

            GUI.Label(new Rect(panel.x, panel.y + 26f, panel.width, 40f), "조작키", headingStyle);

            float y = panel.y + 84f;
            for (int i = 0; i < ControlRows.Length; i++)
            {
                var row = new Rect(panel.x + 40f, y, panel.width - 80f, 30f);
                if (i % 2 == 0) UIKit.Fill(row, new Color(1f, 1f, 1f, 0.035f));

                GUI.Label(new Rect(row.x + 10f, row.y, 130f, row.height), ControlRows[i].key, keyStyle);
                GUI.Label(new Rect(row.x + 150f, row.y, row.width - 160f, row.height), ControlRows[i].action, bodyStyle);
                y += 34f;
            }

            y += 14f;
            UIKit.Fill(new Rect(panel.x + 40f, y, panel.width - 80f, 1f), new Color(1f, 1f, 1f, 0.12f));
            y += 16f;

            GUI.Label(new Rect(panel.x + 50f, y, panel.width - 100f, 26f),
                "경비 시야 콘 색으로 상태를 읽을 수 있다",
                UIKit.Style(17, TextAnchor.MiddleLeft, new Color(0.92f, 0.94f, 0.97f), true));
            y += 30f;

            foreach (var legend in ConeLegend)
            {
                var swatch = new Rect(panel.x + 52f, y + 6f, 16f, 16f);
                UIKit.Fill(swatch, legend.color);
                UIKit.Outline(swatch, new Color(1f, 1f, 1f, 0.25f));
                GUI.Label(new Rect(panel.x + 80f, y, panel.width - 130f, 28f), legend.meaning, mutedStyle);
                y += 26f;
            }

            y += 14f;
            UIKit.Fill(new Rect(panel.x + 40f, y, panel.width - 80f, 1f), new Color(1f, 1f, 1f, 0.12f));
            y += 16f;

            foreach (var tip in TipRows)
            {
                GUI.Label(new Rect(panel.x + 52f, y, 160f, 28f), tip.label,
                    UIKit.Style(15, TextAnchor.MiddleLeft, new Color(0.40f, 0.85f, 1f), true));
                GUI.Label(new Rect(panel.x + 216f, y, panel.width - 262f, 28f), tip.detail, mutedStyle);
                y += 28f;
            }

            DrawButton(closeButton, "닫기", true, new Color(0.35f, 0.85f, 1f));
        }

        void DrawButton(Rect rect, string label, bool enabled, Color accent)
        {
            bool hover = enabled && rect.Contains(mouse);

            UIKit.Fill(rect, hover
                ? new Color(accent.r, accent.g, accent.b, 0.22f)
                : new Color(0.08f, 0.10f, 0.13f, 0.95f));
            UIKit.Outline(rect, hover
                ? new Color(accent.r, accent.g, accent.b, 1f)
                : new Color(accent.r, accent.g, accent.b, 0.45f));

            GUI.Label(rect, label, buttonStyle);
        }
    }
}
