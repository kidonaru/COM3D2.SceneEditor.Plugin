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
        private static string Serialize<T>(T data)
        {
            var serializer = new XmlSerializer(typeof(T));
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
                blendMode = PngBlendMode.Multiply,
                decalFadeAngle = 45f,
                decalProjectOnMaids = true,
            });

            var restored = Deserialize(Serialize(data));

            var png = Assert.Single(restored.pngPlacement.objects);
            Assert.Equal(PngDisplayType.Decal, png.displayType);
            Assert.Equal(PngBlendMode.Multiply, png.blendMode);
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
                decalProjectOnMaids = true,
            };

            var xml = Serialize(png);

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
            Assert.Equal(PngBlendMode.Normal, png.blendMode);
            Assert.Equal(PngDecalProjection.DefaultFadeAngle, png.decalFadeAngle);
            Assert.False(png.decalProjectOnMaids);
        }

        [Fact]
        public void 板でもブレンドが通常以外なら書き出す()
        {
            var png = new ScenePresetPngObject
            {
                source = "config",
                relativePath = "logo.png",
                blendMode = PngBlendMode.Overlay,
            };

            var xml = Serialize(png);
            var restored = Deserialize(Serialize(WithPng(png)));

            Assert.Contains("blendMode=\"Overlay\"", xml);
            Assert.DoesNotContain("displayType", xml);
            Assert.Equal(PngBlendMode.Overlay, Assert.Single(restored.pngPlacement.objects).blendMode);
        }

        [Fact]
        public void 彩度は1以外のときだけ書き出す()
        {
            var plain = Serialize(new ScenePresetPngObject { source = "config", relativePath = "logo.png" });
            var tinted = new ScenePresetPngObject { source = "config", relativePath = "logo.png", saturation = 0.5f };

            var restored = Deserialize(Serialize(WithPng(tinted)));

            Assert.DoesNotContain("saturation", plain);
            Assert.Contains("saturation=\"0.5\"", Serialize(tinted));
            Assert.Equal(0.5f, Assert.Single(restored.pngPlacement.objects).saturation);
        }

        [Fact]
        public void 彩度の属性が無い旧データは1で読む()
        {
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetData><pngPlacement>" +
                "<png source=\"config\" relativePath=\"logo.png\" />" +
                "</pngPlacement></ScenePresetData>";

            var png = Assert.Single(Deserialize(xml).pngPlacement.objects);

            Assert.Equal(1f, png.saturation);
        }
    }
}
