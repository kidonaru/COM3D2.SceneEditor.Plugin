using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView・GameView のシーン描画に重ねるツールバーの部品。
    /// アイコンは正方形、読み込めなかったときは固定幅のテキストにフォールバックする
    /// </summary>
    public static class ViewToolbarDrawer
    {
        public static readonly int TOOLBAR_HEIGHT = 24;
        public static readonly int ITEM_HEIGHT = 20;
        public static readonly int ITEM_MARGIN = 2;
        // アイコン読み込み失敗時のテキスト項目の幅
        public static readonly int TEXT_ITEM_WIDTH = 72;
        // アイコンをボタン枠より少し小さく描くための余白 (両側合計)
        private const float ICON_OFFSET = 4f;
        // シーン描画に重ねる帯の色
        private static readonly Color BG_COLOR = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>項目を横並びに置くビュー。帯の高さの中で縦中央に揃える</summary>
        public static GUIView CreateView(float paddingX)
        {
            return new GUIView
            {
                padding = new Vector2(paddingX, (TOOLBAR_HEIGHT - ITEM_HEIGHT) * 0.5f),
                margin = ITEM_MARGIN,
            };
        }

        public static float GetItemWidth(Texture2D icon)
        {
            return icon != null ? ITEM_HEIGHT : TEXT_ITEM_WIDTH;
        }

        /// <summary>半透明の帯を敷く (ウィンドウローカル座標)</summary>
        public static void DrawBackground(Rect localRect)
        {
            var prevColor = GUI.color;
            GUI.color = BG_COLOR;
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        public static void DrawToggle(
            GUIView view, Texture2D icon, string label, bool value, Action<bool> onChanged)
        {
            if (icon != null)
            {
                view.DrawToggle(icon, value, ITEM_HEIGHT, ITEM_HEIGHT, onChanged, ICON_OFFSET, label);
            }
            else
            {
                view.DrawToggle(label, value, TEXT_ITEM_WIDTH, ITEM_HEIGHT, onChanged);
            }
        }

        /// <summary>押した瞬間だけ true のアイコンボタン</summary>
        public static bool DrawButton(GUIView view, Texture2D icon, string label)
        {
            if (icon == null)
            {
                return view.DrawButton(label, TEXT_ITEM_WIDTH, ITEM_HEIGHT);
            }

            var rect = view.GetDrawRect(ITEM_HEIGHT, ITEM_HEIGHT);
            var clicked = GUI.Button(rect, "", GUIView.gsButton);
            var inset = ICON_OFFSET * 0.5f;
            GUI.DrawTexture(
                new Rect(rect.x + inset, rect.y + inset, rect.width - ICON_OFFSET, rect.height - ICON_OFFSET),
                icon);
            TooltipDrawer.RegisterIfHovered(rect, label);
            view.NextElement(rect);
            return clicked;
        }
    }
}
