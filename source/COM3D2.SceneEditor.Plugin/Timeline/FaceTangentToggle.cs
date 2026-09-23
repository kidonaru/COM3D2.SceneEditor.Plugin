using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情タンジェント補間のトグル ON 時に、未編集のタンジェントだけを既定値へ戻す。
    /// OFF で保存した XML はタンジェントを持たず全値が 0 / 非スムーズで読まれるため、
    /// その状態なら config の既定タンジェントへ揃える。編集済みの値は保持する
    /// </summary>
    public static class FaceTangentToggle
    {
        /// <summary>全キーの in/out が 0 かつ非スムーズなら未編集とみなす。空の列は false</summary>
        public static bool IsUntouched(IEnumerable<ITransformData> transforms)
        {
            var hasAnyTransform = false;
            foreach (var trans in transforms)
            {
                hasAnyTransform = true;
                foreach (var value in trans.tangentValues)
                {
                    if (value.inTangent.isSmooth || value.inTangent.normalizedValue != 0f
                        || value.outTangent.isSmooth || value.outTangent.normalizedValue != 0f)
                    {
                        return false;
                    }
                }
            }
            return hasAnyTransform;
        }

        /// <summary>表情レイヤーのタンジェントが未編集なら既定値へ戻す。isTangentFace を true にした後に呼ぶ</summary>
        public static void ResetIfUntouched(IEnumerable<ITimelineLayer> layers)
        {
            foreach (var layer in layers)
            {
                if (!(layer is MorphTimelineLayer))
                {
                    continue;
                }
                if (IsUntouched(CollectTransforms(layer)))
                {
                    layer.InitTangent();
                }
            }
        }

        private static IEnumerable<ITransformData> CollectTransforms(ITimelineLayer layer)
        {
            foreach (var frame in layer.keyFrames)
            {
                foreach (var bone in frame.bones)
                {
                    if (bone.transform is TransformDataMorph)
                    {
                        yield return bone.transform;
                    }
                }
            }
        }
    }
}
