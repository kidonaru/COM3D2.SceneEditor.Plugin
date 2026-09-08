using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        private void ApplyBloom(MotionData motion, float t)
        {
            var start = motion.start as TransformDataBloom;
            var end = motion.end as TransformDataBloom;

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            var bloom = PEP.PostEffectDataLerp.Lerp(start.bloom, end.bloom, lerpTime);

            postEffectManager.ApplyBloom(bloom);
        }
    }
}
