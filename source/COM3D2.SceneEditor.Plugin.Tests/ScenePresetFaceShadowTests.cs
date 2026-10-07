using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットの顔の影を固定する。
    /// 要素が無い旧プリセットは「触らない」(null)、空要素は「上書きを全解除」
    /// </summary>
    public class ScenePresetFaceShadowTests
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
        public void 上書きから作って戻すと同じになる()
        {
            var overrides = new Dictionary<string, bool>
            {
                { FaceShadowItems.Receive, false },
                { FaceShadowItems.Cast, true },
            };

            var restored = ScenePresetFaceShadow.FromOverrides(overrides).ToOverrides();

            Assert.Equal(2, restored.Count);
            Assert.False(restored[FaceShadowItems.Receive]);
            Assert.True(restored[FaceShadowItems.Cast]);
        }

        [Fact]
        public void 未知の項目と重複は捨てる()
        {
            var preset = new ScenePresetFaceShadow();
            preset.items.Add(new ScenePresetFaceShadowItem { name = "unknown", enabled = true });
            preset.items.Add(new ScenePresetFaceShadowItem { name = FaceShadowItems.Cast, enabled = true });
            preset.items.Add(new ScenePresetFaceShadowItem { name = FaceShadowItems.Cast, enabled = false });

            var overrides = preset.ToOverrides();

            Assert.Single(overrides);
            Assert.True(overrides[FaceShadowItems.Cast]);
        }

        [Fact]
        public void XMLで往復する()
        {
            var maid = new ScenePresetMaid
            {
                faceShadow = ScenePresetFaceShadow.FromOverrides(
                    new Dictionary<string, bool> { { FaceShadowItems.Cast, true } }),
            };

            var text = Serialize(maid);
            Assert.Contains("<faceShadow>", text);
            Assert.Contains("name=\"faceCastShadows\"", text);

            var read = Deserialize(text);
            Assert.True(read.faceShadow.ToOverrides()[FaceShadowItems.Cast]);
        }

        [Fact]
        public void 上書きが無くても空要素を書き全解除として読む()
        {
            var maid = new ScenePresetMaid
            {
                faceShadow = ScenePresetFaceShadow.FromOverrides(new Dictionary<string, bool>()),
            };

            var read = Deserialize(Serialize(maid));

            Assert.NotNull(read.faceShadow);
            Assert.Empty(read.faceShadow.ToOverrides());
        }

        [Fact]
        public void 要素の無い旧プリセットはnullで読む()
        {
            var read = Deserialize(Serialize(new ScenePresetMaid()));

            Assert.Null(read.faceShadow);
        }
    }
}
