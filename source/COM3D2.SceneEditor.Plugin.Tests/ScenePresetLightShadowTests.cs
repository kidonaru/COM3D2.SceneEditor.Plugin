using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトの影の種類を固定する</summary>
    public class ScenePresetLightShadowTests
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
        public void 影なしは属性を書き出さない()
        {
            Assert.DoesNotContain("shadows=", Serialize(new ScenePresetAdditionalLight()));
        }

        [Fact]
        public void 影の種類を往復する()
        {
            var light = new ScenePresetAdditionalLight();
            light.SetShadows(LightShadows.Hard);

            var text = Serialize(light);

            Assert.Contains("shadows=\"1\"", text);
            Assert.Equal(LightShadows.Hard, Deserialize(text).GetShadows());
        }

        [Fact]
        public void 範囲外の値は影なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\" shadows=\"9\" />";

            Assert.Equal(LightShadows.None, Deserialize(text).GetShadows());
        }

        [Fact]
        public void 属性の無い旧プリセットは影なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"0\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.Equal(LightShadows.None, Deserialize(text).GetShadows());
        }
    }
}
