using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightTargetTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        // 影用レイヤー (実機では名前の無い 3 が選ばれる)
        private const int ShadowMask = 1 << 3;

        [Theory]
        [InlineData(LightTargetMode.All, false, ~ShadowMask)]
        [InlineData(LightTargetMode.All, true, -1)]
        [InlineData(LightTargetMode.Character, false, CharacterMask)]
        [InlineData(LightTargetMode.Character, true, CharacterMask | ShadowMask)]
        [InlineData(LightTargetMode.Background, false, ~CharacterMask & ~ShadowMask)]
        [InlineData(LightTargetMode.Background, true, ~CharacterMask)]
        public void モードと影の設定からマスクを組み立てる(LightTargetMode mode, bool characterShadow, int expected)
        {
            Assert.Equal(expected,
                LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All, false)]
        [InlineData(LightTargetMode.All, true)]
        [InlineData(LightTargetMode.Character, false)]
        [InlineData(LightTargetMode.Character, true)]
        [InlineData(LightTargetMode.Background, false)]
        [InlineData(LightTargetMode.Background, true)]
        public void 往復変換で元のモードと影の設定に戻る(LightTargetMode mode, bool characterShadow)
        {
            var mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);

            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask, ShadowMask));
            Assert.Equal(characterShadow, LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Fact]
        public void モードを切り替えても影の設定を引き継ぐ()
        {
            // 背景のみ (ON) → 全て → 背景のみ と、書き込みのたびに今の影ビットを引き継ぐ
            var mask = LightTarget.ToCullingMask(LightTargetMode.Background, true, CharacterMask, ShadowMask);
            foreach (var mode in new[] { LightTargetMode.All, LightTargetMode.Character, LightTargetMode.Background })
            {
                var characterShadow = LightTarget.HasCharacterShadow(mask, ShadowMask);
                mask = LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);
            }

            Assert.Equal(LightTargetMode.Background, LightTarget.FromCullingMask(mask, CharacterMask, ShadowMask));
            Assert.True(LightTarget.HasCharacterShadow(mask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットだけを書き換えてもモードは変わらない(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, ShadowMask);

            var on = LightTarget.WithCharacterShadow(off, true, ShadowMask);

            Assert.Equal(LightTarget.ToCullingMask(mode, true, CharacterMask, ShadowMask), on);
            Assert.Equal(off, LightTarget.WithCharacterShadow(on, false, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 影ビットが0なら影の設定は無視され従来と同じマスクになる(LightTargetMode mode)
        {
            var off = LightTarget.ToCullingMask(mode, false, CharacterMask, 0);
            var on = LightTarget.ToCullingMask(mode, true, CharacterMask, 0);

            Assert.Equal(off, on);
            Assert.False(LightTarget.HasCharacterShadow(on, 0));
            Assert.Equal(mode, LightTarget.FromCullingMask(on, CharacterMask, 0));
        }

        [Fact]
        public void 影ビットが0のマスクは従来の値と同じ()
        {
            Assert.Equal(-1, LightTarget.ToCullingMask(LightTargetMode.All, false, CharacterMask, 0));
            Assert.Equal(CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Character, false, CharacterMask, 0));
            Assert.Equal(~CharacterMask, LightTarget.ToCullingMask(LightTargetMode.Background, false, CharacterMask, 0));
        }

        [Fact]
        public void 想定外のマスクは全て扱い()
        {
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(1 << 10, CharacterMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(0, CharacterMask, ShadowMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(ShadowMask, CharacterMask, ShadowMask));
        }

        [Theory]
        [InlineData(-1, LightTargetMode.All)]
        [InlineData(0, LightTargetMode.All)]
        [InlineData(1, LightTargetMode.Character)]
        [InlineData(2, LightTargetMode.Background)]
        [InlineData(3, LightTargetMode.All)]
        public void 範囲外の整数は全てへ丸める(int value, LightTargetMode expected)
        {
            Assert.Equal(expected, LightTarget.ClampMode(value));
        }

        [Fact]
        public void 対象はindex18()
        {
            var trans = new TransformDataLight();
            Assert.Equal(18, (int)TransformDataLight.Index.LightTarget);
            Assert.True(trans.GetCustomValueInfoMap().ContainsKey("lightTarget"));
        }

        [Fact]
        public void 旧XMLの18値を読むと対象は全て()
        {
            var trans = new TransformDataLight();
            trans.Initialize("Light");
            var xml = new TransformXml
            {
                name = "Light",
                type = TransformType.Light,
                values = new float[18],
                inTangents = new float[18],
                outTangents = new float[18],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
            trans.FromXml(xml);
            Assert.Equal((int)LightTargetMode.All, trans.lightTarget);
        }
    }
}
