using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidScaleTests
    {
        private static MTEP.TransformDataMaidScale CreateKey()
        {
            var trans = new MTEP.TransformDataMaidScale();
            trans.Initialize("Bip01 L Hand");
            return trans;
        }

        private static MTEP.TransformXml CreateXml(float[] values)
        {
            return new MTEP.TransformXml
            {
                name = "Bip01 L Hand",
                type = MTEP.TransformType.MaidScale,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 対象骨は左右の上腕前腕手の6本で表示名を持つ()
        {
            var names = MaidScaleBones.bones.ConvertAll(b => b.boneName);
            Assert.Equal(new[]
            {
                "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand",
                "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand",
            }, names.ToArray());
            Assert.Equal("左手", MaidScaleBones.Find("Bip01 L Hand").displayName);
            Assert.Equal("右上腕", MaidScaleBones.Find("Bip01 R UpperArm").displayName);
        }

        [Fact]
        public void 対象外の骨名はnullを返す()
        {
            Assert.Null(MaidScaleBones.Find("Bip01 Head"));
            Assert.Null(MaidScaleBones.Find(null));
            Assert.Null(MaidScaleBones.Find(""));
        }

        [Theory]
        [InlineData(0f, 0.1f)]
        [InlineData(1.5f, 1.5f)]
        [InlineData(10f, 3f)]
        public void 倍率は範囲内へ丸める(float input, float expected)
        {
            Assert.Equal(expected, MaidScaleBones.Clamp(input));
        }

        [Fact]
        public void キーは1値で既定は倍率1()
        {
            var trans = CreateKey();
            Assert.Equal(MTEP.TransformType.MaidScale, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(1f, trans.multiplier);
            Assert.True(trans.isDefault);
            Assert.Single(trans.tangentValues);

            var info = trans.GetCustomValueInfoMap()["multiplier"];
            Assert.Equal(MaidScaleBones.MinScale, info.min);
            Assert.Equal(MaidScaleBones.MaxScale, info.max);
            Assert.Equal(MaidScaleBones.DefaultScale, info.defaultValue);
        }

        [Fact]
        public void リセットしたキーは倍率1に戻る()
        {
            var trans = CreateKey();
            trans.multiplier = 2f;
            Assert.False(trans.isDefault);

            trans.Reset();
            Assert.Equal(1f, trans.multiplier);
        }

        [Fact]
        public void XMLの往復で倍率を保つ()
        {
            var trans = CreateKey();
            trans.multiplier = 1.75f;

            var loaded = CreateKey();
            loaded.FromXml(trans.ToXml());

            Assert.Equal(1.75f, loaded.multiplier);
            Assert.Equal("Bip01 L Hand", loaded.name);
        }

        [Fact]
        public void XMLの値が倍率としてそのまま読まれる()
        {
            var trans = CreateKey();
            trans.FromXml(CreateXml(new[] { 2.5f }));
            Assert.Equal(2.5f, trans.multiplier);
        }

        [Fact]
        public void 未設定の骨は倍率1で状態なし()
        {
            var state = new MaidScaleState();
            Assert.Equal(1f, state.Get("Bip01 L Hand"));
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 倍率を設定すると丸めて保持し1に戻すと状態なしになる()
        {
            var state = new MaidScaleState();
            state.Set("Bip01 L Hand", 10f);
            Assert.Equal(3f, state.Get("Bip01 L Hand"));
            Assert.False(state.isDefault);
            Assert.Single(state.nonDefaultScales);

            state.Set("Bip01 L Hand", 1f);
            Assert.True(state.isDefault);
            Assert.Empty(state.nonDefaultScales);
        }

        [Fact]
        public void 対象外の骨名への設定は無視する()
        {
            var state = new MaidScaleState();
            state.Set("Bip01 Head", 2f);
            state.Set(null, 2f);
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 自分が書いた値のままなら戻し他から書き換えられていたら戻さない()
        {
            var written = new Vector3(1.35f, 1.5f, 1.5f);
            Assert.True(MaidScaleState.ShouldRestore(written, written));
            // 体型スライダーなどでゲームが書き直した値を、古い退避値で潰さない
            Assert.False(MaidScaleState.ShouldRestore(new Vector3(0.9f, 1f, 1f), written));
        }
    }
}
