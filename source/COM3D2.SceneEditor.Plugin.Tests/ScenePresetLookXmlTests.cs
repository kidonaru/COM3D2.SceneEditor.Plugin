using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの視線 (ScenePresetLook) の保存・読み込み</summary>
    public class ScenePresetLookXmlTests
    {
        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(ScenePresetLook));

        private static ScenePresetLook RoundTrip(ScenePresetLook look)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, look);
                using (var reader = new StringReader(writer.ToString()))
                {
                    return (ScenePresetLook) Serializer.Deserialize(reader);
                }
            }
        }

        [Fact]
        public void タイムライン視線の指定値が往復で保たれる()
        {
            var restored = RoundTrip(new ScenePresetLook
            {
                mode = "オブジェクト",
                lookX = 0.25f,
                lookY = -0.5f,
                timelineTargetType = "Maid",
                timelineTargetIndex = 2,
                timelineMaidPointType = "Chest",
                eyeAngleX = 30f,
                eyeAngleY = 0f,
                eyeAngleZ = -15f,
            });

            Assert.Equal("オブジェクト", restored.mode);
            Assert.Equal(0.25f, restored.lookX);
            Assert.Equal(-0.5f, restored.lookY);
            Assert.Equal("Maid", restored.timelineTargetType);
            Assert.Equal(2, restored.timelineTargetIndex);
            Assert.Equal("Chest", restored.timelineMaidPointType);
            Assert.Equal(30f, restored.eyeAngleX);
            Assert.Equal(0f, restored.eyeAngleY);
            Assert.Equal(-15f, restored.eyeAngleZ);
        }

        [Fact]
        public void 旧プリセットはタイムライン視線が未記録として読める()
        {
            // v25 以前には timeline* 属性が無い。復元側は
            // timelineTargetType が空なら MaidCache へ触らない
            const string legacy =
                "<?xml version=\"1.0\"?>"
                + "<ScenePresetLook mode=\"カメラ\" headToCam=\"true\" eyeToCam=\"true\">"
                + "<lookX>0</lookX><lookY>0</lookY>"
                + "</ScenePresetLook>";

            using (var reader = new StringReader(legacy))
            {
                var look = (ScenePresetLook) Serializer.Deserialize(reader);

                Assert.Equal("カメラ", look.mode);
                Assert.True(look.headToCam);
                Assert.Null(look.timelineTargetType);
                Assert.Null(look.timelineMaidPointType);
                Assert.Equal(0, look.timelineTargetIndex);
            }
        }
    }
}
