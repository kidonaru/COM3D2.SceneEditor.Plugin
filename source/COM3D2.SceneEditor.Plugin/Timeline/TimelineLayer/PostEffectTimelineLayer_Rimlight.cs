using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyRimlight(MotionData motion, float t)
        {
            var start = motion.start as TransformDataRimlight;
            var end = motion.end as TransformDataRimlight;

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            var rimlight = RimlightData.Lerp(start.rimlight, end.rimlight, lerpTime);

            var index = start.index;
            postEffectManager.ApplyRimlight(index, rimlight);
        }

        private List<string> _rimlightNames = new List<string>();
        private List<string> rimlightNames
        {
            get
            {
                var rimlightCount = timeline.rimlightCount;
                if (_rimlightNames.Count != rimlightCount)
                {
                    _rimlightNames.Clear();
                    for (var i = 0; i < rimlightCount; i++)
                    {
                        _rimlightNames.Add(GetRimlightName(i));
                    }
                }

                return _rimlightNames;
            }
        }

        public static bool IsValidRimlightIndex(int index)
        {
            if (index < 0 || index >= timeline.rimlightCount)
            {
                return false;
            }

            return true;
        }

        public static string GetRimlightName(int index)
        {
            if (!IsValidRimlightIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToEffectName(PostEffectType.Rimlight) + suffix;
        }

        public static string GetRimlightJpName(int index)
        {
            if (!IsValidRimlightIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToJpName(PostEffectType.Rimlight) + suffix;
        }
    }
}