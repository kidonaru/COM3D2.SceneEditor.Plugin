using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void TimelineXml_空データを往復シリアライズできる()
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            var src = new TimelineXml();
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                var restored = (TimelineXml)serializer.Deserialize(ms);
                Assert.NotNull(restored);
            }
        }
    }
}
