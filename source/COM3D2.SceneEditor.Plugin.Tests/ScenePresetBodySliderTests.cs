using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの体型スライダー (v41) と、旧メイドスケール (v38) からの変換を固定する</summary>
    public class ScenePresetBodySliderTests
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

        private static KeyValuePair<string, Vector3> Pair(string key, Vector3 values)
        {
            return new KeyValuePair<string, Vector3>(key, values);
        }

        [Fact]
        public void 既定値と定義に無い項目は書き出さない()
        {
            var preset = ScenePresetBodySlider.FromValues(new[]
            {
                Pair("THISCL", new Vector3(1.5f, 1f, 1f)),
                Pair("THIPOS", Vector3.zero),
                Pair("UNKNOWN", new Vector3(2f, 2f, 2f)),
            });

            Assert.Single(preset.parameters);
            Assert.Equal("THISCL", preset.parameters[0].name);
        }

        [Fact]
        public void XMLの往復で値を保ち記録の無い項目は既定値になる()
        {
            var maid = new ScenePresetMaid
            {
                bodySlider = ScenePresetBodySlider.FromValues(new[] { Pair("THIPOS", new Vector3(10f, 0f, -5f)) }),
            };

            var text = Serialize(maid);
            Assert.Contains("<param name=\"THIPOS\" x=\"10\" y=\"0\" z=\"-5\" />", text);

            var loaded = Deserialize(text);
            Assert.Equal(new Vector3(10f, 0f, -5f), loaded.bodySlider.GetValues("THIPOS"));
            Assert.Equal(Vector3.one, loaded.bodySlider.GetValues("THISCL"));
        }

        [Fact]
        public void 全項目が既定でも空要素を書く()
        {
            var maid = new ScenePresetMaid { bodySlider = ScenePresetBodySlider.FromValues(new KeyValuePair<string, Vector3>[0]) };
            var loaded = Deserialize(Serialize(maid));
            Assert.NotNull(loaded.bodySlider);
            Assert.Empty(loaded.bodySlider.parameters);
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            var loaded = Deserialize(Serialize(new ScenePresetMaid()));
            Assert.Null(loaded.bodySlider);
            Assert.Null(loaded.maidScale);
        }

        [Fact]
        public void 手で書き換えた範囲外とNaNの値は丸める()
        {
            var preset = new ScenePresetBodySlider();
            preset.parameters.Add(new ScenePresetBodySliderParam { name = "THISCL", x = 99f, y = float.NaN, z = 1f });

            Assert.Equal(new Vector3(2f, 1f, 1f), preset.GetValues("THISCL"));
        }

        [Fact]
        public void 旧メイドスケールは腕の左右別項目の均一倍率になる()
        {
            var legacy = new ScenePresetMaidScale();
            legacy.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 L UpperArm", scale = 1.5f });
            legacy.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 Head", scale = 2f });

            var converted = ScenePresetBodySlider.FromLegacyMaidScale(legacy);

            Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), converted.GetValues("UPARMSCL_L"));
            Assert.Equal(Vector3.one, converted.GetValues("UPARMSCL_R"));
            Assert.Single(converted.parameters);
        }

        [Fact]
        public void 旧メイドスケールが無ければnull()
        {
            Assert.Null(ScenePresetBodySlider.FromLegacyMaidScale(null));
        }

        [Fact]
        public void 旧プリセットのmaidScale要素を読める()
        {
            const string text =
                "<ScenePresetMaid><maidScale><bone name=\"Bip01 R Hand\" scale=\"2\" /></maidScale></ScenePresetMaid>";
            var loaded = Deserialize(text);

            Assert.Null(loaded.bodySlider);
            var converted = ScenePresetBodySlider.FromLegacyMaidScale(loaded.maidScale);
            Assert.Equal(new Vector3(2f, 2f, 2f), converted.GetValues("HANDSCL_R"));
        }

        [Fact]
        public void 体型スライダーと旧メイドスケールの両方があれば体型スライダーを使う()
        {
            var state = new ScenePresetMaid
            {
                bodySlider = ScenePresetBodySlider.FromValues(new[] { Pair("HANDSCL_R", new Vector3(1.2f, 1.2f, 1.2f)) }),
                maidScale = new ScenePresetMaidScale(),
            };
            state.maidScale.bones.Add(new ScenePresetMaidScaleBone { name = "Bip01 R Hand", scale = 2f });

            Assert.Equal(new Vector3(1.2f, 1.2f, 1.2f), ScenePresetBodySlider.Resolve(state).GetValues("HANDSCL_R"));
        }

        [Fact]
        public void 現行版は41()
        {
            Assert.Equal(41, ScenePresetData.CurrentVersion);
        }
    }
}
