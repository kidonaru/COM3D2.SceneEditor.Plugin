using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ImageOutputSettingXmlTests
    {
        private static TimelineXml RoundTrip(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                using (var reader = new StringReader(writer.ToString()))
                {
                    return (TimelineXml)serializer.Deserialize(reader);
                }
            }
        }

        [Fact]
        public void 倍率と比率がXMLの往復で保持される()
        {
            var xml = new TimelineXml
            {
                imageOutputScale = 3,
                imageOutputAspectMode = GameViewAspectMode.Ratio9x16,
            };
            var loaded = RoundTrip(xml);
            Assert.Equal(3, loaded.imageOutputScale);
            Assert.Equal(GameViewAspectMode.Ratio9x16, loaded.imageOutputAspectMode);
        }

        [Fact]
        public void 要素の無い旧XMLはカスタムと倍率1で読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineData><ImageOutputSize><x>1280</x><y>720</y></ImageOutputSize></TimelineData>";
            var serializer = new XmlSerializer(typeof(TimelineXml));
            TimelineXml loaded;
            using (var reader = new StringReader(text))
            {
                loaded = (TimelineXml)serializer.Deserialize(reader);
            }
            Assert.Equal(GameViewAspectMode.Custom, loaded.imageOutputAspectMode);
            Assert.Equal(1, loaded.imageOutputScale);
            Assert.Equal(1280f, loaded.imageOutputSize.x);
            Assert.Equal(720f, loaded.imageOutputSize.y);
        }
    }
}
