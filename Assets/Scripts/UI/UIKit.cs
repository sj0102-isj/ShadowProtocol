using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// HUD와 메뉴가 공유하는 IMGUI 리소스.
    /// 1차 프로토타입용이며, 에셋 단계에서 uGUI로 교체할 때 함께 사라질 코드다.
    /// </summary>
    public static class UIKit
    {
        static Texture2D whiteTexture;
        static Font koreanFont;

        public static Texture2D White
        {
            get
            {
                if (whiteTexture == null)
                {
                    whiteTexture = new Texture2D(1, 1);
                    whiteTexture.SetPixel(0, 0, Color.white);
                    whiteTexture.Apply();
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
                    koreanFont = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Gulim", "Dotum", "Arial" }, 16);
                    if (koreanFont != null) koreanFont.hideFlags = HideFlags.HideAndDontSave;
                }
                return koreanFont;
            }
        }

        public static GUIStyle Style(int fontSize, TextAnchor anchor, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false,
                normal = { textColor = color }
            };
            if (KoreanFont != null) style.font = KoreanFont;
            return style;
        }

        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        public static void Outline(Rect rect, Color color, float thickness = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
