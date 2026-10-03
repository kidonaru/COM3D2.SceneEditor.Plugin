namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyScreenOverlay(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataScreenOverlay>(motion, t);
            postEffectManager.ApplyScreenOverlay(scratch.screenOverlay);
        }
    }
}
