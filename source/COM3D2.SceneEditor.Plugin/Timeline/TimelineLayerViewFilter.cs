using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表示モードに従ってタイムラインに出すレイヤーを絞る純粋ロジック。
    /// Unity 非依存のジェネリックにしてユニットテスト可能にしている
    /// </summary>
    public static class TimelineLayerViewFilter
    {
        /// <summary>
        /// 表示レイヤーを result へ詰める (result は毎回クリアする)。
        /// カテゴリモードではアクティブレイヤーが targetLayers に無くても必ず含める
        /// (行が消えて編集不能になるのを防ぐ)
        /// </summary>
        public static void Filter<TLayer>(
            IList<TLayer> targetLayers,
            TLayer currentLayer,
            TimelineLayerViewMode mode,
            Func<TLayer, TimelineLayerCategory> getCategory,
            List<TLayer> result)
            where TLayer : class
        {
            result.Clear();
            if (currentLayer == null)
            {
                return;
            }

            if (mode == TimelineLayerViewMode.Layer)
            {
                result.Add(currentLayer);
                return;
            }

            var category = getCategory(currentLayer);
            var containsCurrent = false;
            for (var i = 0; i < targetLayers.Count; i++)
            {
                var layer = targetLayers[i];
                if (getCategory(layer) != category)
                {
                    continue;
                }
                result.Add(layer);
                if (layer == currentLayer)
                {
                    containsCurrent = true;
                }
            }

            if (!containsCurrent)
            {
                result.Add(currentLayer);
            }
        }

        /// <summary>
        /// カテゴリ内で priority が最小のレイヤー。同値なら targetLayers の先に出た方。該当なしは null
        /// </summary>
        public static TLayer FindFirstLayer<TLayer>(
            IList<TLayer> targetLayers,
            TimelineLayerCategory category,
            Func<TLayer, TimelineLayerCategory> getCategory,
            Func<TLayer, int> getPriority)
            where TLayer : class
        {
            TLayer first = null;
            var firstPriority = int.MaxValue;
            for (var i = 0; i < targetLayers.Count; i++)
            {
                var layer = targetLayers[i];
                if (getCategory(layer) != category)
                {
                    continue;
                }
                var priority = getPriority(layer);
                if (first == null || priority < firstPriority)
                {
                    first = layer;
                    firstPriority = priority;
                }
            }
            return first;
        }
    }
}
