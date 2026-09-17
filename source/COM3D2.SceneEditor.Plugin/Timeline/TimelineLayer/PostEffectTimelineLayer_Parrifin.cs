using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyParaffin(MotionData motion, float t)
        {
            // index は使い回しのスクラッチではなく区間開始キーから読む
            var start = motion.start as TransformDataParaffin;
            var scratch = LerpScratch<TransformDataParaffin>(motion, t);
            postEffectManager.ApplyParaffin(start.index, scratch.paraffin);
        }

        private List<string> _paraffinNames = new List<string>();
        private List<string> paraffinNames
        {
            get
            {
                var paraffinCount = timeline.paraffinCount;
                if (_paraffinNames.Count != paraffinCount)
                {
                    _paraffinNames.Clear();
                    for (var i = 0; i < paraffinCount; i++)
                    {
                        _paraffinNames.Add(GetParaffinName(i));
                    }
                }

                return _paraffinNames;
            }
        }

        public static bool IsValidParaffinIndex(int index)
        {
            if (index < 0 || index >= timeline.paraffinCount)
            {
                return false;
            }

            return true;
        }

        public static string GetParaffinName(int index)
        {
            if (!IsValidParaffinIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToEffectName(PostEffectType.Paraffin) + suffix;
        }

        public static string GetParaffinJpName(int index)
        {
            if (!IsValidParaffinIndex(index))
            {
                return "";
            }

            var suffix = PluginUtils.GetGroupSuffix(index);
            return PostEffectUtils.ToJpName(PostEffectType.Paraffin) + suffix;
        }
    }
}