using System;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドアニメ (モーション) レイヤー選択時にどのウィンドウを前面へ出すかの判定。
    /// モーション / IK 両ウィンドウが同じレイヤーへ記録するため、設定で代表を 1 つに絞る
    /// </summary>
    public static class MotionLayerFocusUtils
    {
        public static bool IsMotionLayer(Type layerType)
        {
            return layerType == typeof(MTEP.MotionTimelineLayer)
                || layerType == typeof(MTEP.AnimationTimelineLayer);
        }

        /// <summary>レイヤーがモーション系で、かつ self が設定上の前面表示先なら true</summary>
        public static bool ShouldFocus(Type layerType, MTEP.MotionLayerFocusWindow self)
        {
            return IsMotionLayer(layerType) && MTEP.ConfigManager.instance.config.motionLayerFocusWindow == self;
        }
    }
}
