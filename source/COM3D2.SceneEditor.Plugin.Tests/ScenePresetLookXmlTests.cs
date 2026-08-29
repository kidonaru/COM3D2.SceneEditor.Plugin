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
                timelineLookX = 0.3f,
                timelineLookY = -0.15f,
            });

            Assert.Equal("オブジェクト", restored.mode);
            Assert.Equal(0.25f, restored.lookX);
            Assert.Equal(-0.5f, restored.lookY);
            Assert.Equal("Maid", restored.timelineTargetType);
            Assert.Equal(2, restored.timelineTargetIndex);
            Assert.Equal("Chest", restored.timelineMaidPointType);
            Assert.Equal(0.3f, restored.timelineLookX);
            Assert.Equal(-0.15f, restored.timelineLookY);
        }

        [Fact]
        public void 瞳回転時代のeyeAngle属性は読み飛ばされる()
        {
            // 瞳回転 (eyeAngle*) は顔向き (timelineLook*) へ一本化して撤去した。
            // 撤去前に保存されたプリセットの同属性は未知属性として無視される
            const string withEyeAngle =
                "<?xml version=\"1.0\"?>"
                + "<ScenePresetLook mode=\"カメラ\" timelineTargetType=\"None\""
                + " eyeAngleX=\"30\" eyeAngleY=\"0\" eyeAngleZ=\"-15\">"
                + "<lookX>0</lookX><lookY>0</lookY>"
                + "</ScenePresetLook>";

            using (var reader = new StringReader(withEyeAngle))
            {
                var look = (ScenePresetLook) Serializer.Deserialize(reader);

                Assert.Equal("None", look.timelineTargetType);
                Assert.Equal(0f, look.timelineLookX);
                Assert.Equal(0f, look.timelineLookY);
            }
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
