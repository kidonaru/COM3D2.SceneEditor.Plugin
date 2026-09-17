using System.IO;
using System.Xml.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// v33 以前のライト補間トグル (IsLightColorEasing / IsLightExtraEasing /
    /// IsLightCompatibilityMode) は v34 で廃止された。
    /// 旧 XML はエラーなく読め、保存時にはこれらの要素が書き出されないことを固定する
    /// </summary>
    public class LightEasingToggleMigrationTests
    {
        private const string OldXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<TimelineData version=\"33\">" +
            "<IsLightColorEasing>false</IsLightColorEasing>" +
            "<IsLightExtraEasing>false</IsLightExtraEasing>" +
            "<IsLightCompatibilityMode>false</IsLightCompatibilityMode>" +
            "</TimelineData>";

        private static TimelineXml Deserialize(string xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(xml))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        [Fact]
        public void 旧ライト補間トグルは読み込めて値を保持する()
        {
            var xml = Deserialize(OldXml);

            Assert.Equal(33, xml.version);
            Assert.False(xml.isLightColorEasing);
            Assert.False(xml.isLightExtraEasing);
            Assert.False(xml.isLightCompatibilityMode);
        }

        [Fact]
        public void 旧ライト補間トグルは保存時に書き出されない()
        {
            var xml = Deserialize(OldXml);

            var root = XDocument.Parse(Serialize(xml)).Root;

            Assert.Null(root.Element("IsLightColorEasing"));
            Assert.Null(root.Element("IsLightExtraEasing"));
            Assert.Null(root.Element("IsLightCompatibilityMode"));
        }
    }
}
