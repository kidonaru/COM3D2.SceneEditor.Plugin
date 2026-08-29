using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>レイヤー型 → ITimelineItemInspector の登録辞書</summary>
    public static class TimelineItemInspectorRegistry
    {
        private static readonly Dictionary<Type, ITimelineItemInspector> _map =
            new Dictionary<Type, ITimelineItemInspector>();

        /// <summary>登録する。同じ型の再登録は置き換え</summary>
        public static void Register(Type layerType, ITimelineItemInspector inspector)
        {
            _map[layerType] = inspector;
        }

        public static ITimelineItemInspector Find(Type layerType)
        {
            ITimelineItemInspector inspector;
            return _map.TryGetValue(layerType, out inspector) ? inspector : null;
        }

        public static ITimelineItemInspector Find(MTEP.ITimelineLayer layer)
        {
            return layer != null ? Find(layer.GetType()) : null;
        }

        /// <summary>
        /// 選択中メニュー項目からセット行を子へ展開し、重複を除いた葉項目だけを result へ集める。
        /// BoneMenuManager.GetSelectedItems はセット行(全子選択時)と子項目の両方を含むため、
        /// 表示前にこの正規化を通す
        /// </summary>
        public static void CollectLeafItems(
            IList<MTEP.IBoneMenuItem> source, List<MTEP.IBoneMenuItem> result)
        {
            result.Clear();
            foreach (var item in source)
            {
                if (item.isSetMenu)
                {
                    if (item.children == null)
                    {
                        continue;
                    }
                    foreach (var child in item.children)
                    {
                        if (!result.Contains(child))
                        {
                            result.Add(child);
                        }
                    }
                }
                else if (!result.Contains(item))
                {
                    result.Add(item);
                }
            }
        }
    }
}
