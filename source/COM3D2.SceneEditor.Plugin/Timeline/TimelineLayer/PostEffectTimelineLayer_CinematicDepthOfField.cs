namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyCinematicDepthOfField(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataCinematicDepthOfField>(motion, t);
            postEffectManager.ApplyCinematicDepthOfField(scratch.cinematicDepthOfField);
        }
    }
}
