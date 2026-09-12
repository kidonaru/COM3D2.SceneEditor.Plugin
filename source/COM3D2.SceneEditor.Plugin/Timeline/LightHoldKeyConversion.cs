using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// v33 以前のライト補間トグル (色補間 / 拡張補間) が OFF だったタイムラインを、
    /// 常時補間の v34 で同じ見え方になるよう「保持キー」を挿入して変換する。
    /// 旧挙動では OFF のチャンネルは区間開始キーの値で固定され、次キーで段差になっていた。
    /// </summary>
    public static class LightHoldKeyConversion
    {
        /// <summary>この変換が導入されたタイムラインバージョン</summary>
        public const int IntroducedVersion = 34;

        /// <summary>
        /// 「値が変わった」とみなす最小差。スライダー編集の丸め程度の差 (例: 71.2 と 71.19843) で
        /// 見た目に影響しない保持キーが増えるのを防ぐ
        /// </summary>
        public const float ChangeTolerance = 0.01f;

        private static readonly TransformDataLight.Index[] ExtraIndices =
        {
            TransformDataLight.Index.Range,
            TransformDataLight.Index.Intensity,
            TransformDataLight.Index.SpotAngle,
            TransformDataLight.Index.ShadowStrength,
            TransformDataLight.Index.ShadowBias,
        };

        private static readonly TransformDataLight.Index[] ColorIndices =
        {
            TransformDataLight.Index.ColorR,
            TransformDataLight.Index.ColorG,
            TransformDataLight.Index.ColorB,
        };

        /// <summary>補間対象外で常に開始キーの値を引き継ぐチャンネル</summary>
        private static readonly TransformDataLight.Index[] StepIndices =
        {
            TransformDataLight.Index.Easing,
            TransformDataLight.Index.MaidSlotNo,
            TransformDataLight.Index.Visible,
            TransformDataLight.Index.LightTarget,
        };

        /// <summary>旧 XML のトグル状態から変換が必要かを返す</summary>
        public static bool IsRequired(int version, bool isLightColorEasing, bool isLightExtraEasing)
        {
            return version < IntroducedVersion && (!isLightColorEasing || !isLightExtraEasing);
        }

        /// <summary>
        /// ライトレイヤーへ保持キーを挿入し、挿入した数を返す。
        /// 値が変わる区間の終端キー 1 フレーム前へ開始キーの値を持つキーを置き、
        /// 終端キーへの 1 フレーム区間を段差の代わりにする。
        /// 補間の評価にタンジェント値を使うため、レイヤーの anm 生成 (UpdateTangent) 後に呼ぶこと。
        /// </summary>
        public static int ConvertLayer(
            ITimelineLayer layer,
            TimelineData timeline,
            bool holdColor,
            bool holdExtra)
        {
            if (layer == null || timeline == null || (!holdColor && !holdExtra))
            {
                return 0;
            }

            var sequences = new Dictionary<string, List<BoneData>>();
            foreach (var frame in layer.keyFrames)
            {
                foreach (var bone in frame.bones)
                {
                    if (!(bone.transform is TransformDataLight))
                    {
                        continue;
                    }
                    if (!sequences.TryGetValue(bone.name, out var sequence))
                    {
                        sequence = new List<BoneData>();
                        sequences[bone.name] = sequence;
                    }
                    sequence.Add(bone);
                }
            }

            var insertedCount = 0;
            foreach (var sequence in sequences.Values)
            {
                sequence.Sort((a, b) => a.frameNo - b.frameNo);

                for (var i = 0; i < sequence.Count - 1; i++)
                {
                    var startBone = sequence[i];
                    var endBone = sequence[i + 1];
                    var holdFrameNo = endBone.frameNo - 1;
                    if (holdFrameNo <= startBone.frameNo)
                    {
                        continue;
                    }

                    var start = (TransformDataLight)startBone.transform;
                    var end = (TransformDataLight)endBone.transform;
                    var t = (float)(holdFrameNo - startBone.frameNo) / (endBone.frameNo - startBone.frameNo);
                    var t0 = startBone.frameNo * timeline.frameDuration;
                    var t1 = endBone.frameNo * timeline.frameDuration;

                    var holdTrans = CreateHoldKey(start, end, t0, t1, t, holdColor, holdExtra);
                    if (holdTrans == null)
                    {
                        continue;
                    }

                    var frame = layer.GetOrCreateFrame(holdFrameNo);
                    frame.SetBone(frame.CreateBone(holdTrans));
                    insertedCount++;
                }
            }

            return insertedCount;
        }

        /// <summary>
        /// 区間 [start, end] の補間位置 t (0〜1) に置く保持キーを作る。
        /// 保持対象のチャンネルは start の値、それ以外は現行の再生と同じ補間値を持つ。
        /// 保持対象に変化が無い区間は null を返す (キーを増やさない)。
        /// </summary>
        public static TransformDataLight CreateHoldKey(
            TransformDataLight start,
            TransformDataLight end,
            float t0,
            float t1,
            float t,
            bool holdColor,
            bool holdExtra)
        {
            var holdIndices = new HashSet<int>();
            if (holdExtra)
            {
                AddChangedIndices(holdIndices, start, end, ExtraIndices);
            }
            if (holdColor)
            {
                AddChangedIndices(holdIndices, start, end, ColorIndices);
            }
            if (holdIndices.Count == 0)
            {
                return null;
            }

            var trans = TimelineManager.CreateTransform<TransformDataLight>(start.name);

            // 再生と同じ経路で補間値を求める (位置 / 回転は Hermite、色は線形)
            trans.position = PluginUtils.HermiteVector3(t0, t1, start.positionValues, end.positionValues, t);
            trans.rotation = PluginUtils.HermiteQuaternion(t0, t1, start.rotationValues, end.rotationValues, t);
            trans.color = Color.Lerp(start.color, end.color, t);
            foreach (var index in ExtraIndices)
            {
                trans.values[(int)index].value = PluginUtils.HermiteValue(
                    t0, t1, start.values[(int)index], end.values[(int)index], t);
            }

            // 補間対象外のチャンネルはトグル指定に関わらず常に開始キーの値を引き継ぐ
            foreach (var index in StepIndices)
            {
                trans.values[(int)index].value = start.values[(int)index].value;
            }
            foreach (var index in holdIndices)
            {
                trans.values[index].value = start.values[index].value;
            }

            // 挿入キーのタンジェントは前後から自動計算に任せる
            foreach (var value in trans.values)
            {
                value.inTangent.isSmooth = true;
                value.outTangent.isSmooth = true;
            }

            return trans;
        }

        private static void AddChangedIndices(
            HashSet<int> result,
            TransformDataLight start,
            TransformDataLight end,
            TransformDataLight.Index[] indices)
        {
            foreach (var index in indices)
            {
                var diff = end.values[(int)index].value - start.values[(int)index].value;
                if (Mathf.Abs(diff) > ChangeTolerance)
                {
                    result.Add((int)index);
                }
            }
        }
    }
}
