using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのメイドスケール (v38) を固定する</summary>
    public class ScenePresetMaidScaleTests
    {
        private static string Serialize(ScenePresetMaid maid)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, maid);
                return writer.ToString();
            }
        }

        private static ScenePresetMaid Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetMaid));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetMaid)serializer.Deserialize(reader);
            }
        }

        private static KeyValuePair<string, float> Pair(string name, float scale)
        {
            return new KeyValuePair<string, float>(name, scale);
        }

        [Fact]
        public void 倍率1と対象外の骨は書き出さない()
        {
            var preset = ScenePresetMaidScale.FromScales(new[]
            {
                Pair("Bip01 L Hand", 1.5f),
                Pair("Bip01 R Hand", 1f),
                Pair("Bip01 Head", 2f),
            });

            Assert.Single(preset.bones);
            Assert.Equal("Bip01 L Hand", preset.bones[0].name);
            Assert.Equal(1.5f, preset.bones[0].scale);
        }

        [Fact]
        public void XMLの往復で倍率を保ち記録の無い骨は1になる()
        {
            var maid = new ScenePresetMaid
            {
                maidScale = ScenePresetMaidScale.FromScales(new[] { Pair("Bip01 L Hand", 1.5f) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<bone name=\"Bip01 L Hand\" scale=\"1.5\" />", text);

            var loaded = Deserialize(text);
            Assert.Equal(1.5f, loaded.maidScale.GetScale("Bip01 L Hand"));
            Assert.Equal(1f, loaded.maidScale.GetScale("Bip01 R Hand"));
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaid><visible>true</visible></ScenePresetMaid>";

            Assert.Null(Deserialize(text).maidScale);
        }

        [Fact]
        public void 全骨1でも空要素を書き空リストとして読む()
        {
            var maid = new ScenePresetMaid
            {
                maidScale = ScenePresetMaidScale.FromScales(new[] { Pair("Bip01 L Hand", 1f) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<maidScale />", text);

            var loaded = Deserialize(text);
            Assert.NotNull(loaded.maidScale);
            Assert.Empty(loaded.maidScale.bones);
        }

        [Fact]
        public void 同じ骨が複数あれば先の値を使う()
        {
            var preset = new ScenePresetMaidScale();
            preset.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L Hand", scale = 1.5f });
            preset.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L Hand", scale = 2f });

            Assert.Equal(1.5f, preset.GetScale("Bip01 L Hand"));
        }

        [Theory]
        [InlineData("NaN", 1f)]
        [InlineData("10", 3f)]
        [InlineData("0", 0.1f)]
        public void 不正な倍率は範囲へ丸めNaNは1にする(string scaleText, float expected)
        {
            var text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetMaid><maidScale><bone name=\"Bip01 L Hand\" scale=\"" + scaleText + "\" />" +
                "</maidScale></ScenePresetMaid>";

            Assert.Equal(expected, Deserialize(text).maidScale.GetScale("Bip01 L Hand"));
        }
    }
}
