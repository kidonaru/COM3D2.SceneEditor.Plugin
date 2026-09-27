using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LightCookieDataTests
    {
        [Fact]
        public void 既定値は既定の輪郭と既定の硬さ()
        {
            var data = LightCookieData.Default;

            Assert.Equal(LightCookieMode.Default, data.mode);
            Assert.Equal(LightCookieData.DefaultHardness, data.hardness);
            Assert.Equal("", data.image);
        }

        [Fact]
        public void 正規化は未知のモードを既定にし硬さを丸める()
        {
            var data = new LightCookieData { mode = (LightCookieMode)99, hardness = 3f, image = null }.Normalized();

            Assert.Equal(LightCookieMode.Default, data.mode);
            Assert.Equal(1f, data.hardness);
            Assert.Equal("", data.image);
        }

        [Fact]
        public void NaNの硬さは既定値にする()
        {
            var data = new LightCookieData { mode = LightCookieMode.Generated, hardness = float.NaN }.Normalized();

            Assert.Equal(LightCookieData.DefaultHardness, data.hardness);
        }

        [Fact]
        public void 画像名のnullと空文字は等しい()
        {
            var a = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = null };
            var b = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = "" };

            Assert.True(a.Equals(b));
        }
    }
}
