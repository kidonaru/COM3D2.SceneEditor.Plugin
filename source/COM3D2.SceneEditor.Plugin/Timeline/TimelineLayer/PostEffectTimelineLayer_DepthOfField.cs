using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyDepthOfField(MotionData motion, float t)
        {
            var start = motion.start as TransformDataDepthOfField;
            var end = motion.end as TransformDataDepthOfField;

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            var depthOfField = DepthOfFieldData.Lerp(start.depthOfField, end.depthOfField, lerpTime);

            postEffectManager.ApplyDepthOfField(depthOfField);
        }
    }
}