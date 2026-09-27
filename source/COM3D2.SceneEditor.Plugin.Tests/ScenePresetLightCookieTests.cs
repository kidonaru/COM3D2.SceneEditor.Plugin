using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトの輪郭属性を固定する</summary>
    public class ScenePresetLightCookieTests
    {
        private static string Serialize(ScenePresetAdditionalLight light)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, light);
                return writer.ToString();
            }
        }

        private static ScenePresetAdditionalLight Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetAdditionalLight));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetAdditionalLight)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 既定の輪郭は属性を書き出さない()
        {
            var text = Serialize(new ScenePresetAdditionalLight());

            Assert.DoesNotContain("cookie", text);
        }

        [Fact]
        public void 画像指定を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetCookie(new LightCookieData { mode = LightCookieMode.Image, hardness = 0.5f, image = "window.png" });

            var text = Serialize(light);
            var restored = Deserialize(text).GetCookie();

            Assert.Contains("cookieImage=\"window.png\"", text);
            Assert.DoesNotContain("cookieHardness", text);
            Assert.Equal(LightCookieMode.Image, restored.mode);
            Assert.Equal("window.png", restored.image);
        }

        [Fact]
        public void 硬さ指定を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetCookie(new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.25f });

            var restored = Deserialize(Serialize(light)).GetCookie();

            Assert.Equal(LightCookieMode.Generated, restored.mode);
            Assert.Equal(0.25f, restored.hardness);
        }

        [Fact]
        public void 属性の無い旧プリセットは既定の輪郭として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.True(Deserialize(text).GetCookie().Equals(LightCookieData.Default));
        }
    }
}
