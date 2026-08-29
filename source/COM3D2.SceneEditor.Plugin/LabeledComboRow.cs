using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「ラベル + コンボボックス」1 行の描画。
    /// MaidWindowBase (各ウィンドウ) と Inspector の項目表示で共有する。
    /// コンボ幅は残り幅から求めるため、ビューを縮めてもボタンがはみ出さない
    /// </summary>
    public static class LabeledComboRow
    {
        /// <summary>矢印ボタン 2 つ分の幅。GUIComboBox が showArrow 時に確保する値と合わせる</summary>
        private const float ComboArrowWidth = 40f;

        /// <summary>コンボをビュー幅に合わせて縮めるときの下限</summary>
        private const float MinComboWidth = 80f;

        /// <summary>ドロップダウンの縦幅</summary>
        private const float ComboContentHeight = 300f;

        /// <summary>ラベル + コンボの 1 行でコンボに使える幅</summary>
        public static float CalcComboWidth(GUIView view, float labelWidth)
        {
            return Mathf.Max(
                view.viewRect.width - view.padding.x * 2
                    - labelWidth - view.margin - ComboArrowWidth,
                MinComboWidth);
        }

        /// <param name="trailingWidth">コンボの後ろに置くコントロールのために空ける幅</param>
        /// <param name="drawTrailing">コンボの後ろに置くコントロールの描画</param>
        public static void Draw<T>(
            GUIView view,
            string label,
            GUIComboBox<T> comboBox,
            float labelWidth,
            float rowHeight,
            float trailingWidth = 0f,
            Action drawTrailing = null)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel(label, labelWidth, rowHeight, style: GUIView.gsLabelRight);

                var comboWidth = CalcComboWidth(view, labelWidth) - trailingWidth;
                comboBox.buttonSize = new Vector2(comboWidth, rowHeight);
                comboBox.contentSize = new Vector2(comboWidth, ComboContentHeight);
                comboBox.DrawButton(view);

                if (drawTrailing != null)
                {
                    drawTrailing();
                }
            }
            view.EndLayout();
        }
    }
}
