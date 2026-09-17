using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ラベル + XYZ (ドラッグラベル + 数値入力) + リセットボタンの 1 行。
    /// Object・モデルボーンなど Transform を編集する行描画で共有する
    /// </summary>
    public static class Vector3RowDrawer
    {
        public static void Draw(
            GUIView view,
            string label,
            float dragSensitivity,
            float labelWidth,
            float rowHeight,
            Vector3 value,
            Action<Vector3> onChanged,
            Action onReset)
        {
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = labelWidth,
                height = rowHeight,
                dragSensitivity = dragSensitivity,
                value = value,
                onChanged = onChanged,
                onReset = onReset,
            });
        }
    }
}
