using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライト定義の輪郭 (&lt;CookieMode&gt; ほか) の保存と読込を固定する。
    /// 既定の輪郭は書き出さず、要素の無い旧 XML は既定として読む
    /// </summary>
    public class LightCookieXmlTests
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
        public void 既定の輪郭は書き出さない()
        {
            var data = new TimelineLightData { name = "Light2", type = LightType.Spot };

            var text = Serialize(data.ToXml());

            Assert.DoesNotContain("Cookie", text);
        }

        [Fact]
        public void 硬さ指定は種類だけを往復し硬さは定義に書かない()
        {
            // 硬さはライトキー (index 19) で持つ
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Spot,
                cookie = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.95f, image = "a.png" },
            };

            var text = Serialize(data.ToXml());
            var restored = FromText(text);

            Assert.Contains("<CookieMode>1</CookieMode>", text);
            Assert.DoesNotContain("CookieHardness", text);
            Assert.DoesNotContain("CookieImage", text);
            Assert.Equal(LightCookieMode.Generated, restored.cookie.mode);
            Assert.Equal(LightCookieData.DefaultHardness, restored.cookie.hardness);
        }

        [Fact]
        public void 開発中の定義にあるCookieHardnessは読み飛ばす()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type>" +
                "<CookieMode>1</CookieMode><CookieHardness>0.3</CookieHardness></TimelineLightXml>");

            Assert.Equal(LightCookieMode.Generated, restored.cookie.mode);
            Assert.Equal(LightCookieData.DefaultHardness, restored.cookie.hardness);
        }

        [Fact]
        public void 硬さだけが違う輪郭は定義として同じ()
        {
            var a = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.2f, image = "" };
            var b = new LightCookieData { mode = LightCookieMode.Generated, hardness = 0.9f, image = "" };
            var c = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.2f, image = "a.png" };

            Assert.True(a.EqualsIgnoringHardness(b));
            Assert.False(a.EqualsIgnoringHardness(c));
            Assert.False(c.EqualsIgnoringHardness(new LightCookieData { mode = LightCookieMode.Image, image = "b.png" }));
        }

        [Fact]
        public void 画像指定は画像名だけを往復する()
        {
            var data = new TimelineLightData
            {
                name = "Light2",
                type = LightType.Spot,
                cookie = new LightCookieData { mode = LightCookieMode.Image, hardness = 0.3f, image = @"gobo\window.png" },
            };

            var text = Serialize(data.ToXml());
            var restored = FromText(text);

            Assert.Contains("<CookieImage>gobo\\window.png</CookieImage>", text);
            Assert.DoesNotContain("CookieHardness", text);
            Assert.Equal(LightCookieMode.Image, restored.cookie.mode);
            Assert.Equal(@"gobo\window.png", restored.cookie.image);
        }

        [Fact]
        public void 要素の無い旧XMLは既定の輪郭として読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type></TimelineLightXml>");

            Assert.True(restored.cookie.Equals(LightCookieData.Default));
        }

        [Fact]
        public void 未知のモードは既定として読む()
        {
            var restored = FromText(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineLightXml><Name>Light2</Name><Type>Spot</Type><CookieMode>9</CookieMode></TimelineLightXml>");

            Assert.Equal(LightCookieMode.Default, restored.cookie.mode);
        }
    }
}
