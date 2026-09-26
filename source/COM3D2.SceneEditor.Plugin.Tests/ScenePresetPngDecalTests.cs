using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットの PNG 配置に追加した表示タイプとデカール設定の保存と読込を固定する。
    /// 板では属性を書き出さず、属性の無い旧データは板として読む
    /// </summary>
    public class ScenePresetPngDecalTests
    {
        private static string Serialize(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static ScenePresetData Deserialize(string xml)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader(xml))
            {
                return (ScenePresetData)serializer.Deserialize(reader);
            }
        }

        private static ScenePresetData WithPng(ScenePresetPngObject png)
        {
            var data = new ScenePresetData { pngPlacement = new ScenePresetPngPlacement() };
            data.pngPlacement.objects.Add(png);
            return data;
        }

        [Fact]
        public void デカール設定は往復で保たれる()
        {
            var data = WithPng(new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                displayType = PngDisplayType.Decal,
                decalBlendMode = PngDecalBlendMode.Multiply,
                decalFadeAngle = 45f,
                decalProjectOnMaids = true,
            });

            var restored = Deserialize(Serialize(data));

            var png = Assert.Single(restored.pngPlacement.objects);
            Assert.Equal(PngDisplayType.Decal, png.displayType);
            Assert.Equal(PngDecalBlendMode.Multiply, png.decalBlendMode);
            Assert.Equal(45f, png.decalFadeAngle);
            Assert.True(png.decalProjectOnMaids);
        }

        [Fact]
        public void 板では表示タイプとデカール設定を書き出さない()
        {
            // ScenePresetData 全体だと動画設定の displayType と混ざるため PNG 1 枚だけを直列化する
            var png = new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                decalBlendMode = PngDecalBlendMode.Additive,
                decalProjectOnMaids = true,
            };

            string xml;
            var serializer = new XmlSerializer(typeof(ScenePresetPngObject));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, png);
                xml = writer.ToString();
            }

            Assert.DoesNotContain("displayType", xml);
            Assert.DoesNotContain("decal", xml);
        }

        [Fact]
        public void 属性の無い旧データは板と既定値で読む()
        {
            // v35 以前の形式 (表示タイプ・デカール設定の属性が無い)
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetData><pngPlacement>" +
                "<png source=\"config\" relativePath=\"logo.png\" billboard=\"true\" />" +
                "</pngPlacement></ScenePresetData>";

            var restored = Deserialize(xml);

            var png = Assert.Single(restored.pngPlacement.objects);
            Assert.Equal(PngDisplayType.Board, png.displayType);
            Assert.Equal(PngDecalBlendMode.Normal, png.decalBlendMode);
            Assert.Equal(PngDecalProjection.DefaultFadeAngle, png.decalFadeAngle);
            Assert.False(png.decalProjectOnMaids);
        }
    }
}
