using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデル定義の表示レイヤー (&lt;ModelLayer&gt;) の保存と読込を固定する。
    /// 未指定 (-1) は書き出さず、要素の無い旧 XML は未指定として読む
    /// </summary>
    public class ModelLayerXmlTests
    {
        private static string Serialize(TimelineModelXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineModelXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineModelXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 指定したレイヤーはModelLayer要素として往復する()
        {
            var data = new TimelineModelData { name = "cup.menu", pluginName = "ModItemExplorer", layer = 10 };

            var text = Serialize(data.ToXml());
            var restored = new TimelineModelData();
            restored.FromXml(Deserialize(text));

            Assert.Contains("<ModelLayer>10</ModelLayer>", text);
            Assert.Equal(10, restored.layer);
        }

        [Fact]
        public void 未指定のレイヤーは書き出さない()
        {
            var data = new TimelineModelData { name = "cup.menu", pluginName = "ModItemExplorer" };

            var text = Serialize(data.ToXml());

            Assert.Equal(StudioModelStat.UnspecifiedLayer, data.layer);
            Assert.DoesNotContain("ModelLayer", text);
        }

        [Fact]
        public void 要素の無い旧XMLは未指定として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelineModelXml><Name>cup.menu</Name><PluginName>ModItemExplorer</PluginName></TimelineModelXml>";

            var restored = new TimelineModelData();
            restored.FromXml(Deserialize(text));

            Assert.Equal(StudioModelStat.UnspecifiedLayer, restored.layer);
        }

        [Fact]
        public void レイヤー0も指定として書き出す()
        {
            // Unity の Default レイヤーは 0。未指定 (-1) と区別して保存する
            var data = new TimelineModelData { name = "cup.menu", layer = 0 };

            Assert.Contains("<ModelLayer>0</ModelLayer>", Serialize(data.ToXml()));
        }
    }
}
