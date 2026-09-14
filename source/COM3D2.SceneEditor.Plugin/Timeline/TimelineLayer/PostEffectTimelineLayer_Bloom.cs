namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyBloom(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataBloom>(motion, t);
            postEffectManager.ApplyBloom(scratch.bloom);
        }
    }
}
