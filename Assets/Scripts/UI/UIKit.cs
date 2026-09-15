using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// HUD와 메뉴가 공유하는 IMGUI 리소스.
    /// 1차 프로토타입용이며, 에셋 단계에서 uGUI로 교체할 때 함께 사라질 코드다.
    /// </summary>
    //
    // ── IMGUI 가 뭔가 ──
    // 유니티에는 UI 만드는 방식이 두 가지 있다.
    //   uGUI    : 씬에 Canvas·Image·Text 오브젝트를 배치하는 방식. 일반적인 게임 UI
    //   IMGUI   : OnGUI() 안에서 코드로 매 프레임 직접 그리는 방식. 원래 에디터 툴용
    // 이 프로젝트는 UI를 전부 코드로 그리는 IMGUI 를 썼다. 씬에 UI 오브젝트가 하나도 없어서
    // StageBuilder 가 씬을 통째로 다시 만들어도 UI가 깨지지 않기 때문이다.
    // 대신 성능이 나쁘고 애니메이션이 어려워서, 에셋을 붙이는 단계에서 uGUI 로 바꿀 예정이다.
    public static class UIKit
    {
        static Texture2D whiteTexture;
        static Font koreanFont;

        /// <summary>1×1 흰색 이미지. 색을 바꿔가며 사각형을 칠하는 데 쓴다.</summary>
        //
        // 지연 초기화(lazy initialization) 패턴: 처음 쓰일 때 만들고 그다음부터는 만든 것을 재사용.
        // static 이라 한 번 만들면 게임이 끝날 때까지 남는다.
        public static Texture2D White
        {
            get
            {
                if (whiteTexture == null)
                {
                    whiteTexture = new Texture2D(1, 1);
                    whiteTexture.SetPixel(0, 0, Color.white);
                    whiteTexture.Apply();   // SetPixel 후 Apply 를 불러야 실제로 반영된다

                    // HideAndDontSave = 하이어라키에 안 보이고 씬 저장에도 안 들어간다.
                    // 코드로 만든 임시 리소스에 붙이는 표시다.
                    whiteTexture.hideFlags = HideFlags.HideAndDontSave;
                }
                return whiteTexture;
            }
        }

        /// <summary>
        /// 기본 IMGUI 폰트(Arial)에는 한글 글리프가 없어 네모로 깨진다.
        /// OS에 설치된 한글 폰트를 순서대로 시도한다.
        /// </summary>
        public static Font KoreanFont
        {
            get
            {
                if (koreanFont == null)
                {
                    // 배열에 여러 이름을 넣으면 앞에서부터 있는 것을 쓴다.
                    // 맑은 고딕이 없는 컴퓨터에서도 굴림·돋움으로 대체되어 글자가 보인다.
                    koreanFont = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Gulim", "Dotum", "Arial" }, 16);
                    if (koreanFont != null) koreanFont.hideFlags = HideFlags.HideAndDontSave;
                }
                return koreanFont;
            }
        }

        /// <summary>글자 스타일을 한 줄로 만든다. 색·크기·정렬만 넘기면 된다.</summary>
        //
        // bool bold = false 처럼 기본값을 주면 호출할 때 생략할 수 있다.
        //   Style(20, TextAnchor.MiddleCenter, Color.white)        → 보통 글씨
        //   Style(20, TextAnchor.MiddleCenter, Color.white, true)  → 굵은 글씨
        public static GUIStyle Style(int fontSize, TextAnchor anchor, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false,
                // 중첩 초기화자. style.normal.textColor = color 와 같은 뜻이다.
                normal = { textColor = color }
            };
            if (KoreanFont != null) style.font = KoreanFont;
            return style;
        }

        /// <summary>사각형을 단색으로 칠한다.</summary>
        public static void Fill(Rect rect, Color color)
        {
            // GUI.color 는 전역 설정이라, 바꾸고 나서 원래대로 되돌려야 한다.
            // 안 되돌리면 이후에 그리는 모든 것이 이 색으로 물든다.
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        /// <summary>사각형 테두리를 그린다. 얇은 사각형 네 개를 변마다 붙이는 방식.</summary>
        public static void Outline(Rect rect, Color color, float thickness = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);                      // 위
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);       // 아래
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);                     // 왼쪽
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);      // 오른쪽
        }
    }
}
