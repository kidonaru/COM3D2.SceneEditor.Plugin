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
            var scratch = LerpScratch<TransformDataDepthOfField>(motion, t);
            postEffectManager.ApplyDepthOfField(scratch.depthOfField);
        }
    }
}