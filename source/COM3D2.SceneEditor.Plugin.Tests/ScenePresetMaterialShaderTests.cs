using System.IO;
using System.Xml.Serialization;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのマテリアルのシェーダー変更を固定する</summary>
    public class ScenePresetMaterialShaderTests
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
        public void 変更が無ければ属性を書き出さない()
        {
            Assert.DoesNotContain("shader=", Serialize(new ScenePresetMaterial { owner = "wear", material = "a" }));
        }

        [Fact]
        public void シェーダーを往復する()
        {
            var text = Serialize(new ScenePresetMaterial
            {
                owner = "wear", material = "a", shader = "com3d2mod/Standard_NPRToonV2_Lit_",
            });

            Assert.Contains("shader=\"com3d2mod/Standard_NPRToonV2_Lit_\"", text);
            Assert.Equal("com3d2mod/Standard_NPRToonV2_Lit_", Deserialize(text).shader);
        }

        [Fact]
        public void 属性の無い旧プリセットはシェーダーを持たない()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaterial owner=\"wear\" material=\"a\" index=\"0\" />";

            Assert.Null(Deserialize(text).shader);
        }

        [Fact]
        public void シェーダーだけの変更も空ではない()
        {
            Assert.True(new ScenePresetMaterial().isEmpty);
            Assert.False(new ScenePresetMaterial { shader = "CM3D2/Lighted" }.isEmpty);
        }
    }
}
