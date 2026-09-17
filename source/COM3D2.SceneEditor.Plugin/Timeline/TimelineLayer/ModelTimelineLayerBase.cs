using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    public abstract class ModelTimelineLayerBase : TimelineLayerBase
    {
        protected ModelTimelineLayerBase(int slotNo) : base(slotNo)
        {
        }
    }
}