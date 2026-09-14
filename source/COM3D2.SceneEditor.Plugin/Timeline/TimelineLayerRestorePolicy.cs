using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// レイヤー削除時にシーン状態を復元してよいかの判定。
    /// 判定の根拠は各レイヤーの TimelineLayerDesc 属性にあり、ここはその読み手。
    /// Unity 実体に触れない純粋ロジックなのでテストから直接呼べる
    /// </summary>
    public static class TimelineLayerRestorePolicy
    {
        /// <summary>
        /// 属性が無い型は復元しない。宣言漏れのレイヤーでシーンを触ると
        /// 「何が戻ったのか分からない」事故になるため、安全側へ倒す
        /// </summary>
        public static bool CanRestoreOnRemove(Type layerType)
        {
            if (layerType == null)
            {
                return false;
            }

            var attr = layerType.GetCustomAttribute<TimelineLayerDescAttribute>();
            return attr != null && attr.CanRestoreOnRemove;
        }
    }
}
