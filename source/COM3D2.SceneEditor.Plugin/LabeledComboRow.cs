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
        /// <param name="comboCount">
        /// 同じ行に並べるコンボの個数。2 個目以降は矢印 1 組とコンボ間のマージンを余分に要する。
        /// 戻り値は全コンボの合計幅なので、呼び出し側で分配する
        /// </param>
        public static float CalcComboWidth(GUIView view, float labelWidth, int comboCount = 1)
        {
            return Mathf.Max(
                view.viewRect.width - view.padding.x * 2
                    - labelWidth - view.margin
                    - ComboArrowWidth * comboCount - view.margin * (comboCount - 1),
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

                DrawCombo(view, comboBox, CalcComboWidth(view, labelWidth) - trailingWidth, rowHeight);

                if (drawTrailing != null)
                {
                    drawTrailing();
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 指定個数のコンボを 1 行に並べても、それぞれが下限幅を保てるか。
        /// 保てない幅で並べると各コンボが下限幅へクランプされて行からはみ出すため、
        /// 呼び出し側はここが false の間は行を分ける
        /// </summary>
        public static bool CanFitCombos(GUIView view, float labelWidth, int comboCount)
        {
            return CalcComboWidth(view, labelWidth, comboCount) >= MinComboWidth * comboCount;
        }

        /// <summary>幅を指定してコンボボックスのボタンを描く (1 行に複数並べる呼び出し用)</summary>
        public static void DrawCombo<T>(
            GUIView view, GUIComboBox<T> comboBox, float comboWidth, float rowHeight)
        {
            comboWidth = Mathf.Max(comboWidth, MinComboWidth);
            comboBox.buttonSize = new Vector2(comboWidth, rowHeight);
            comboBox.contentSize = new Vector2(comboWidth, ComboContentHeight);
            comboBox.DrawButton(view);
        }
    }
}
