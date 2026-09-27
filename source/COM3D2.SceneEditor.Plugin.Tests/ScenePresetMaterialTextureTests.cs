using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのマテリアルのテクスチャ差し替えを固定する</summary>
    public class ScenePresetMaterialTextureTests
    {
        private static string Serialize(ScenePresetMaterial material)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaterial));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, material);
                return writer.ToString();
            }
        }

        private static ScenePresetMaterial Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaterial));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaterial)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 差し替えが無ければ要素を書き出さない()
        {
            Assert.DoesNotContain("<texture", Serialize(new ScenePresetMaterial { owner = "body", material = "skin" }));
        }

        [Fact]
        public void テクスチャ差し替えを往復する()
        {
            var material = new ScenePresetMaterial { owner = "body", material = "skin" };
            material.textures.Add(new ScenePresetMaterialTexture { prop = "_ShadowRateToon", file = "Toon/0_影なし.png" });

            var text = Serialize(material);
            Assert.Contains("<texture prop=\"_ShadowRateToon\" file=\"Toon/0_影なし.png\" />", text);

            var restored = Deserialize(text);
            Assert.Single(restored.textures);
            Assert.Equal("Toon/0_影なし.png", restored.textures[0].file);
        }

        [Fact]
        public void テクスチャだけでも空ではない()
        {
            var material = new ScenePresetMaterial { owner = "body", material = "skin" };
            Assert.True(material.isEmpty);

            material.textures.Add(new ScenePresetMaterialTexture { prop = "_ToonRamp", file = "Toon/a.png" });
            Assert.False(material.isEmpty);
        }

        [Fact]
        public void 要素の無い旧プリセットはテクスチャなしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><ScenePresetMaterial owner=\"body\" material=\"skin\" index=\"0\" />");

            Assert.Empty(restored.textures);
        }
    }
}
