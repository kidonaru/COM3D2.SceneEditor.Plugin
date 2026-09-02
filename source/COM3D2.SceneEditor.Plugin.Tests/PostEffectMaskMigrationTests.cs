using System.Collections.Generic;
using System.Linq;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 旧 (COM3D2 版) タイムラインのリムライト/パラフィンに含まれる Depth 系 3 値は
    /// COM3D2.5 版で廃止され、同じ位置がマスク設定に置き換わっている。
    /// そのまま読み込むと Depth 値がマスク設定として解釈されてしまうため、
    /// 移行処理が値数を詰めて既定値へ戻すことを固定する
    /// </summary>
    public class PostEffectMaskMigrationTests
    {
        private static MTEP.TimelineXml CreatePostEffectXml(
            MTEP.TransformType type, float[] values)
        {
            var xml = new MTEP.TimelineXml();
            xml.layers.Add(new MTEP.TimelineLayerXml
            {
                className = "PostEffectTimelineLayer",
                keyFrames = new List<MTEP.FrameXml>
                {
                    new MTEP.FrameXml
                    {
                        bones = new List<MTEP.BoneXml>
                        {
                            new MTEP.BoneXml
                            {
                                transform = new MTEP.TransformXml
                                {
                                    name = type.ToString(),
                                    type = type,
                                    values = values,
                                },
                            },
                        },
                    },
                },
            });
            return xml;
        }

        private static float[] GetValues(MTEP.TimelineXml xml)
        {
            return xml.layers[0].keyFrames[0].bones[0].transform.values;
        }

        [Fact]
        public void 旧リムライトのDepth値がマスク既定値へ変換される()
        {
            // index 16-18 は旧 DepthMin/DepthMax/DepthFade。値数を昇順にして混同を防ぐ
            var oldValues = Enumerable.Range(0, MTEP.TimelineXml.OldRimlightValueCount)
                .Select(i => (float)i).ToArray();
            var xml = CreatePostEffectXml(MTEP.TransformType.Rimlight, oldValues);

            xml.ConvertPostEffectMaskValues();

            var trans = MTEP.TransformDataRimlight.defaultTrans;
            var values = GetValues(xml);
            Assert.Equal(trans.valueCount, values.Length);
            Assert.Equal(trans.maskModeInfo.defaultValue,
                values[(int)MTEP.TransformDataRimlight.Index.MaskMode]);
            Assert.Equal(trans.excludeFaceInfo.defaultValue,
                values[(int)MTEP.TransformDataRimlight.Index.ExcludeFace]);
            Assert.Equal(trans.applyHairInfo.defaultValue,
                values[(int)MTEP.TransformDataRimlight.Index.ApplyHair]);

            // マスク設定以外は旧データの値がそのまま残る
            Assert.Equal(15f, values[(int)MTEP.TransformDataRimlight.Index.FadeExp]);
            Assert.Equal(19f, values[(int)MTEP.TransformDataRimlight.Index.UseNormal]);
            Assert.Equal(24f, values[(int)MTEP.TransformDataRimlight.Index.IsWorldSpace]);
        }

        [Fact]
        public void 旧パラフィンのDepth値がマスク既定値へ変換される()
        {
            var oldValues = Enumerable.Range(0, MTEP.TimelineXml.OldParaffinValueCount)
                .Select(i => (float)i).ToArray();
            var xml = CreatePostEffectXml(MTEP.TransformType.Paraffin, oldValues);

            xml.ConvertPostEffectMaskValues();

            var trans = MTEP.TransformDataParaffin.defaultTrans;
            var values = GetValues(xml);
            Assert.Equal(trans.valueCount, values.Length);
            Assert.Equal(trans.maskModeInfo.defaultValue,
                values[(int)MTEP.TransformDataParaffin.Index.MaskMode]);
            Assert.Equal(20f, values[(int)MTEP.TransformDataParaffin.Index.UseSubstruct]);
        }

        [Fact]
        public void 現行形式のリムライトは変換されない()
        {
            var trans = MTEP.TransformDataRimlight.defaultTrans;
            var currentValues = Enumerable.Range(0, trans.valueCount)
                .Select(i => (float)i).ToArray();
            var xml = CreatePostEffectXml(MTEP.TransformType.Rimlight, currentValues);

            xml.ConvertPostEffectMaskValues();

            Assert.Equal(currentValues, GetValues(xml));
        }
    }
}
