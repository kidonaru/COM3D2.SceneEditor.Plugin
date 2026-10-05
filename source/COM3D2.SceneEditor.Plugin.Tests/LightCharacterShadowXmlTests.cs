using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義のキャラの影 (&lt;CharacterShadow&gt;) の保存と読込を固定する。
    /// OFF は書き出さず、要素の無い旧 XML は OFF として読む (今までの見た目を変えない)
    /// </summary>
    public class LightCharacterShadowXmlTests
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
        public void OFFは書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Directional };

            Assert.DoesNotContain("CharacterShadow", Serialize(data.ToXml()));
        }

        [Fact]
        public void ONを往復する()
        {
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Directional,
                shadows = LightShadows.Soft,
                characterShadow = true,
            };

            var text = Serialize(data.ToXml());

            Assert.Contains("<CharacterShadow>true</CharacterShadow>", text);
            var restored = FromText(text);
            Assert.True(restored.characterShadow);
            Assert.Equal(LightShadows.Soft, restored.shadows);
        }

        [Fact]
        public void 要素の無い旧XMLはOFFとして読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Directional</Type><Shadows>2</Shadows></TimelineLightXml>");

            Assert.False(restored.characterShadow);
        }
    }
}
