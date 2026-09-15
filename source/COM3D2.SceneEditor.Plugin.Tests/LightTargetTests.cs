using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightTargetTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        [Fact]
        public void 全て_は全レイヤー()
        {
            Assert.Equal(-1, LightTarget.ToCullingMask(LightTargetMode.All, CharacterMask));
        }

        [Fact]
        public void キャラのみ_はキャラ用マスク()
        {
            Assert.Equal(CharacterMask,
                LightTarget.ToCullingMask(LightTargetMode.Character, CharacterMask));
        }

        [Fact]
        public void 背景のみ_はキャラ用マスクの補集合()
        {
            Assert.Equal(~CharacterMask,
                LightTarget.ToCullingMask(LightTargetMode.Background, CharacterMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        [InlineData(LightTargetMode.Background)]
        public void 往復変換で元のモードに戻る(LightTargetMode mode)
        {
            var mask = LightTarget.ToCullingMask(mode, CharacterMask);
            Assert.Equal(mode, LightTarget.FromCullingMask(mask, CharacterMask));
        }

        [Fact]
        public void 想定外のマスクは全て扱い()
        {
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(1 << 10, CharacterMask));
            Assert.Equal(LightTargetMode.All, LightTarget.FromCullingMask(0, CharacterMask));
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
        public void ライトキーの値数は19で末尾が対象()
        {
            var trans = new TransformDataLight();
            Assert.Equal(19, trans.valueCount);
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
