using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのメニュー項目選択を Inspector に表示するファサード。
    /// 表示の優先順位は InspectorWindow 側の分岐が決める
    /// (キーフレーム選択 = KeyFrameInspector が本クラスより優先)
    /// </summary>
    public static class TimelineItemInspector
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.BoneMenuManager boneMenuManager => MTEP.BoneMenuManager.Instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static readonly List<MTEP.IBoneMenuItem> _leafItems =
            new List<MTEP.IBoneMenuItem>(64);

        /// <summary>メニュー項目選択中で、現在レイヤーのプロバイダが居るか</summary>
        public static bool ShouldDraw()
        {
            if (timelineManager.timeline == null || timelineManager.currentLayer == null)
            {
                return false;
            }
            // 簡易表示の GetSelectedItems は疑似項目 EasyMenuItem を常に返すため対象外
            if (timelineConfig.isEasyEdit)
            {
                return false;
            }
            if (TimelineItemInspectorRegistry.Find(timelineManager.currentLayer) == null)
            {
                return false;
            }
            return boneMenuManager.GetSelectedItems().Count > 0;
        }

        public static void Draw(GUIView view)
        {
            var layer = timelineManager.currentLayer;
            var inspector = TimelineItemInspectorRegistry.Find(layer);
            if (inspector == null)
            {
                return;
            }

            TimelineItemInspectorRegistry.CollectLeafItems(
                boneMenuManager.GetSelectedItems(), _leafItems);
            inspector.DrawItems(view, layer, _leafItems);
        }
    }
}
