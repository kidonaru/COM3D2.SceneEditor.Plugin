using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの追加ライトのキャラの影を固定する</summary>
    public class ScenePresetLightCharacterShadowTests
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
        public void OFFは属性を書き出さない()
        {
            Assert.DoesNotContain("characterShadow=", Serialize(new ScenePresetAdditionalLight()));
        }

        [Fact]
        public void ONを往復する()
        {
            var text = Serialize(new ScenePresetAdditionalLight { characterShadow = true });

            Assert.Contains("characterShadow=\"true\"", text);
            Assert.True(Deserialize(text).characterShadow);
        }

        [Fact]
        public void 属性の無い旧プリセットはOFFとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetAdditionalLight type=\"1\" target=\"2\" shadows=\"2\"><intensity>1</intensity></ScenePresetAdditionalLight>";

            Assert.False(Deserialize(text).characterShadow);
        }

        [Fact]
        public void シーンプリセットの版は39()
        {
            Assert.Equal(39, ScenePresetData.CurrentVersion);
        }
    }
}
