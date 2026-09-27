using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライトキーに追加した輪郭の硬さ (index 19) を固定する。
    /// 旧データ (19 値以下) は不足分が 0 で埋まるため、補正しないと最も柔らかい輪郭で読まれる
    /// </summary>
    public class LightCookieHardnessKeyTests
    {
        private static TransformDataLight CreateKey()
        {
            var trans = new TransformDataLight();
            trans.Initialize("Light1");
            return trans;
        }

        private static TransformXml CreateXml(float[] values)
        {
            return new TransformXml
            {
                name = "Light1",
                type = TransformType.Light,
                values = values,
                inTangents = new float[values.Length],
                outTangents = new float[values.Length],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };
        }

        [Fact]
        public void 硬さはindex19で値数は20()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            Assert.Equal(19, (int)TransformDataLight.Index.CookieHardness);
            Assert.Equal(20, CreateKey().valueCount);
            Assert.Equal(19, TransformDataLight.LegacyValueCount);
            Assert.True(CreateKey().GetCustomValueInfoMap().ContainsKey("cookieHardness"));
        }

        [Fact]
        public void リセットしたキーの硬さは既定値()
        {
            var trans = CreateKey();
            trans.Reset();

            Assert.Equal(LightCookieData.DefaultHardness, trans.cookieHardness);
        }

        [Theory]
        [InlineData(18)]
        [InlineData(19)]
        public void 硬さを持たない旧キーは既定値で読む(int valueCount)
        {
            var trans = CreateKey();
            trans.FromXml(CreateXml(new float[valueCount]));

            Assert.Equal(LightCookieData.DefaultHardness, trans.cookieHardness);
        }

        [Fact]
        public void 硬さを持つキーは保存値で読む()
        {
            var values = new float[20];
            values[19] = 0.25f;
            var trans = CreateKey();
            trans.FromXml(CreateXml(values));

            Assert.Equal(0.25f, trans.cookieHardness);
        }

        [Fact]
        public void 硬さは往復で保たれる()
        {
            var trans = CreateKey();
            trans.cookieHardness = 0.4f;

            var restored = CreateKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(0.4f, restored.cookieHardness);
        }
    }
}
