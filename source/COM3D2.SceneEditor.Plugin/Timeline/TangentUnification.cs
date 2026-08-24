using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ロード直後のタイムラインから easing 補間を取り除き、Tangent 補間へ一本化する。
    ///
    /// 旧フォーマット (MTE 産 XML を含む) は区間終端キーの easing 値で補間形状を持っていた。
    /// これを EasingToTangent で正規化 Tangent へ写し、以後は全レイヤーが Hermite で再生される
    /// </summary>
    public static class TangentUnification
    {
        /// <summary>タイムライン全体を Tangent 補間へ変換する</summary>
        public static void ConvertTimeline(TimelineData timeline)
        {
            if (timeline == null)
            {
                return;
            }

            foreach (var layer in timeline.layers)
            {
                ConvertLayer(layer);
            }

            // 変換後は easing モードが残らないよう、全カテゴリを Tangent モードへ倒す
            // (hasEasing はこれらのフラグの否定で定義されているため、以後は常に false になる)
            timeline.isTangentCamera = true;
            timeline.isTangentLight = true;
            timeline.isTangentMove = true;
            timeline.isTangentModel = true;
            timeline.isTangentModelBone = true;
            timeline.isTangentModelShapeKey = true;
        }

        private static void ConvertLayer(ITimelineLayer layer)
        {
            if (layer == null)
            {
                return;
            }

            // ボーン名ごとにキーフレーム順の transform 列を作る
            var sequences = new Dictionary<string, List<ITransformData>>();
            var order = new List<string>();

            foreach (var frame in layer.keyFrames)
            {
                foreach (var bone in frame.bones)
                {
                    if (bone.transform == null)
                    {
                        continue;
                    }
                    if (!sequences.TryGetValue(bone.name, out var sequence))
                    {
                        sequence = new List<ITransformData>();
                        sequences[bone.name] = sequence;
                        order.Add(bone.name);
                    }
                    sequence.Add(bone.transform);
                }
            }

            foreach (var boneName in order)
            {
                ConvertTransformSequence(sequences[boneName]);
            }
        }

        /// <summary>1 ボーンのキーフレーム列を Tangent 化する</summary>
        public static void ConvertTransformSequence(IList<ITransformData> transforms)
        {
            if (transforms == null || transforms.Count == 0)
            {
                return;
            }

            // easing を持たない (元から Tangent 補間の) 型は触らない
            if (!transforms[0].hasEasing)
            {
                return;
            }

            for (var i = 1; i < transforms.Count; i++)
            {
                // easing は区間終端キーが持つ。区間開始キーの outTangent とペアで書き込む
                var easing = (MoveEasingType)transforms[i].easing;
                ApplyEasingTangent(transforms[i - 1].values, transforms[i].values, easing);
                transforms[i].easing = 0;
            }

            // 先頭キーの流入区間と末尾キーの流出区間は存在しないため線形で埋める
            SetLinearTangent(transforms[0].values, isOut: false);
            SetLinearTangent(transforms[transforms.Count - 1].values, isOut: true);
            transforms[0].easing = 0;
        }

        /// <summary>区間の easing を、開始キーの outTangent と終端キーの inTangent へ写す</summary>
        public static void ApplyEasingTangent(
            ValueData[] prevValues, ValueData[] currentValues, MoveEasingType easing)
        {
            var pair = EasingToTangent.Convert(easing);

            foreach (var value in prevValues)
            {
                value.outTangent.normalizedValue = pair.outTangent;
                value.outTangent.isSmooth = false;
            }

            foreach (var value in currentValues)
            {
                value.inTangent.normalizedValue = pair.inTangent;
                value.inTangent.isSmooth = false;
            }
        }

        /// <summary>線形補間 (normalizedValue = 1) を明示的に書き込む。
        /// 既定値 0 のままだとフラット補間になってしまうため省略できない</summary>
        public static void SetLinearTangent(ValueData[] values, bool isOut)
        {
            foreach (var value in values)
            {
                var tangent = isOut ? value.outTangent : value.inTangent;
                tangent.normalizedValue = 1f;
                tangent.isSmooth = false;
            }
        }
    }
}
