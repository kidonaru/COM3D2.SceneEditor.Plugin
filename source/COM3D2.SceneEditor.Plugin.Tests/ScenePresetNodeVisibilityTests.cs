using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットのノード表示を固定する。
    /// 要素が無い旧プリセットは「触らない」(null)、空要素は「上書きを全解除」
    /// </summary>
    public class ScenePresetNodeVisibilityTests
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

        [Fact]
        public void 対象外のノードは書き出さない()
        {
            var preset = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>
            {
                { "Mune_L", false },
                { "Unknown", false },
            });

            Assert.Single(preset.nodes);
            Assert.Equal("Mune_L", preset.nodes[0].name);
            Assert.False(preset.nodes[0].visible);
        }

        [Fact]
        public void 上書きを往復する()
        {
            var maid = new ScenePresetMaid
            {
                nodeVisibility = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>
                {
                    { "Mune_L", false },
                    { "Bip01 Head", true },
                }),
            };

            var text = Serialize(maid);

            Assert.Contains("<node name=\"Mune_L\" visible=\"false\" />", text);
            var overrides = Deserialize(text).nodeVisibility.ToOverrides();
            Assert.Equal(2, overrides.Count);
            Assert.False(overrides["Mune_L"]);
            Assert.True(overrides["Bip01 Head"]);
        }

        [Fact]
        public void 上書きが無くても空要素を書き全解除として読める()
        {
            var maid = new ScenePresetMaid
            {
                nodeVisibility = ScenePresetNodeVisibility.FromOverrides(new Dictionary<string, bool>()),
            };

            var restored = Deserialize(Serialize(maid));

            Assert.NotNull(restored.nodeVisibility);
            Assert.Empty(restored.nodeVisibility.ToOverrides());
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            var restored = Deserialize(Serialize(new ScenePresetMaid()));

            Assert.Null(restored.nodeVisibility);
        }

        [Fact]
        public void 手で書き換えた重複と対象外のノードは読込時に捨てる()
        {
            var preset = new ScenePresetNodeVisibility();
            preset.nodes.Add(new ScenePresetNode { name = "Mune_L", visible = false });
            preset.nodes.Add(new ScenePresetNode { name = "Mune_L", visible = true });
            preset.nodes.Add(new ScenePresetNode { name = "Unknown", visible = false });
            preset.nodes.Add(null);

            var overrides = preset.ToOverrides();

            Assert.Single(overrides);
            Assert.False(overrides["Mune_L"]);
        }
    }
}
