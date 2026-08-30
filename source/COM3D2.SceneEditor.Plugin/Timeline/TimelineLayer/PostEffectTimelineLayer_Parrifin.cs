// ポストエフェクトの値クラスは PostEffects.Plugin 側の実体を使う。alias の理由は PostEffectsBridge を参照
extern alias PostEffectsPlugin;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;
using PEP = PostEffectsPlugin::COM3D25.PostEffects.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyParaffin(MotionData motion, float t)
        {
            var start = motion.start as TransformDataParaffin;
            var end = motion.end as TransformDataParaffin;

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            var paraffin = PEP.ColorParaffinData.Lerp(start.paraffin, end.paraffin, lerpTime);

            var index = start.index;
            postEffectManager.ApplyParaffin(index, paraffin);
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