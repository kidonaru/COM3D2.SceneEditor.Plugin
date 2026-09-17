using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyDistanceFog(MotionData motion, float t)
        {
            // index は使い回しのスクラッチではなく区間開始キーから読む
            var start = motion.start as TransformDataDistanceFog;
            var scratch = LerpScratch<TransformDataDistanceFog>(motion, t);
            postEffectManager.ApplyDistanceFog(start.index, scratch.distanceFog);
        }

        private List<string> _distanceFogNames = new List<string>();
        private List<string> distanceFogNames
        {
            get
            {
                var distanceFogCount = timeline.distanceFogCount;
                if (_distanceFogNames.Count != distanceFogCount)
                {
                    _distanceFogNames.Clear();
                    for (var i = 0; i < distanceFogCount; i++)
                    {
                        _distanceFogNames.Add(GetDistanceFogName(i));
                    }
                }

                return _distanceFogNames;
            }
        }

        public static bool IsValidDistanceFogIndex(int index)
        {
            if (index < 0 || index >= timeline.distanceFogCount)
            {
                return false;
            }

            return true;
        }

        public static string GetDistanceFogName(int index)
        {
            if (!IsValidDistanceFogIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToEffectName(PostEffectType.DistanceFog) + suffix;
        }

        public static string GetDistanceFogJpName(int index)
        {
            if (!IsValidDistanceFogIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToJpName(PostEffectType.DistanceFog) + suffix;
        }
    }
}