using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットのカメラの身長補正を固定する</summary>
    public class ScenePresetCameraHeightTests
    {
        private static string Serialize(ScenePresetCamera camera)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetCamera));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, camera);
                return writer.ToString();
            }
        }

        private static ScenePresetCamera Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetCamera));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetCamera)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 補正なしは属性を書き出さない()
        {
            var text = Serialize(new ScenePresetCamera());

            Assert.DoesNotContain("heightSlotNo=", text);
            Assert.DoesNotContain("heightBase=", text);
        }

        [Fact]
        public void 基準メイドと基準身長を往復する()
        {
            var restored = Deserialize(Serialize(new ScenePresetCamera { heightSlotNo = 2, heightBase = 158 }));

            Assert.Equal(2, restored.heightSlotNo);
            Assert.Equal(158, restored.heightBase);
            Assert.True(restored.hasHeight);
        }

        [Fact]
        public void 基準身長が未設定でも基準メイドは往復する()
        {
            var text = Serialize(new ScenePresetCamera { heightSlotNo = 0 });

            Assert.Contains("heightSlotNo=\"0\"", text);
            Assert.DoesNotContain("heightBase=", text);
            Assert.Equal(0, Deserialize(text).heightSlotNo);
        }

        [Fact]
        public void 属性の無い旧プリセットは補正なしとして読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<ScenePresetCamera maidSlotNo=\"-1\" maidPointType=\"0\" followRotation=\"false\"" +
                " shakeFrequencyScale=\"0\" shakeSeed=\"0\"><distance>2</distance></ScenePresetCamera>";

            var restored = Deserialize(text);

            Assert.Equal(-1, restored.heightSlotNo);
            Assert.Equal(0, restored.heightBase);
            Assert.False(restored.hasHeight);
        }

        [Fact]
        public void シーンプリセットの版は身長補正の追加後()
        {
            Assert.True(ScenePresetData.CurrentVersion >= 43);
        }
    }
}
