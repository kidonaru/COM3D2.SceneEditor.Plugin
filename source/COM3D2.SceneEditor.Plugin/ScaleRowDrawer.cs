using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 拡縮行 1 行の描画。連動トグルの状態は Object・ボーン・タイムライン項目で共有する
    /// (連動の計算は GUIView 側に集約済み)
    /// </summary>
    public static class ScaleRowDrawer
    {
        public const float DragSensitivity = 0.01f;

        private static Config config => ConfigManager.instance.config;

        public static void Draw(
            GUIView view,
            Vector3 value,
            float labelWidth,
            float rowHeight,
            Action<Vector3> onChanged,
            Action onReset)
        {
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "拡縮",
                labelWidth = labelWidth,
                height = rowHeight,
                dragSensitivity = DragSensitivity,
                value = value,
                onChanged = onChanged,
                onReset = onReset,
                linkIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Link),
                linked = config.inspectorScaleLinked,
                onLinkChanged = OnLinkChanged,
            });
        }

        private static void OnLinkChanged(bool on)
        {
            config.inspectorScaleLinked = on;
            config.dirty = true;
        }
    }
}
