using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義の影の種類 (&lt;Shadows&gt;) の保存と読込を固定する。
    /// 影なしは書き出さず、要素の無い旧 XML は影なしとして読む
    /// </summary>
    public class LightShadowXmlTests
    {
        private static string Serialize(TimelineLightXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineLightData FromText(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineLightXml));
            using (var reader = new StringReader(text))
            {
                var restored = new TimelineLightData();
                restored.FromXml((TimelineLightXml)serializer.Deserialize(reader));
                return restored;
            }
        }

        [Fact]
        public void 影なしは書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot };

            Assert.DoesNotContain("Shadows", Serialize(data.ToXml()));
        }

        [Fact]
        public void 影の種類を往復する()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot, shadows = LightShadows.Soft };

            var text = Serialize(data.ToXml());

            Assert.Contains("<Shadows>2</Shadows>", text);
            Assert.Equal(LightShadows.Soft, FromText(text).shadows);
        }

        [Fact]
        public void 要素の無い旧XMLは影なしとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type></TimelineLightXml>");

            Assert.Equal(LightShadows.None, restored.shadows);
        }

        [Fact]
        public void 範囲外の値は影なしとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type><Shadows>9</Shadows></TimelineLightXml>");

            Assert.Equal(LightShadows.None, restored.shadows);
        }
    }
}
