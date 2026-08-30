using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ラベル + 数値入力 + 増減ボタンの 1 行。
    /// テキスト表示数・サブカメラ数・ポストエフェクト数など、
    /// 要素の個数を増減する行描画で共有する
    /// </summary>
    public static class CountRowDrawer
    {
        /// <summary>増減ボタン 1 個の幅 (2 個分を数値入力の幅から差し引く)</summary>
        private const float StepButtonWidth = 20f;

        public static void Draw(
            GUIView view,
            string label,
            float rowHeight,
            int value,
            int min,
            int max,
            Action<int> onChanged)
        {
            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel(label, view.labelWidth, rowHeight);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = value,
                    width = view.viewRect.width -
                        (view.labelWidth + StepButtonWidth * 2 + view.padding.x * 2),
                    height = rowHeight,
                    onChanged = x => onChanged(Mathf.Clamp(x, min, max)),
                });

                if (view.DrawButton("-", StepButtonWidth, rowHeight, value > min))
                {
                    onChanged(value - 1);
                }
                if (view.DrawButton("+", StepButtonWidth, rowHeight, value < max))
                {
                    onChanged(value + 1);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();
        }
    }
}
