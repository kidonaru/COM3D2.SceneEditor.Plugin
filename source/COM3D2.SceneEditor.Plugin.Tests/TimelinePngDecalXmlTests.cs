using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムライン XML の PNG 実体 (&lt;PngObjects&gt;) に追加した表示タイプとデカール設定を固定する。
    /// 板では要素を書き出さず、要素の無い XML (MTE 産・旧 SE) は板として読む
    /// </summary>
    public class TimelinePngDecalXmlTests
    {
        private static string Serialize(TimelinePngObjectXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelinePngObjectXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelinePngObjectXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelinePngObjectXml));
            using (var reader = new StringReader(text))
            {
                return (TimelinePngObjectXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void デカール設定は往復で保たれる()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                displayType = (int)PngDisplayType.Decal,
                decalBlendMode = (int)PngDecalBlendMode.Additive,
                decalFadeAngle = 30f,
                decalProjectOnMaids = true,
            };

            var text = Serialize(data.ToXml());
            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Contains("<DisplayType>1</DisplayType>", text);
            Assert.Equal((int)PngDisplayType.Decal, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Additive, restored.decalBlendMode);
            Assert.Equal(30f, restored.decalFadeAngle);
            Assert.True(restored.decalProjectOnMaids);
        }

        [Fact]
        public void 板ではデカールの要素を書き出さない()
        {
            var data = new TimelinePngObjectData
            {
                imageName = "logo",
                renderQueue = 3000,
                decalBlendMode = (int)PngDecalBlendMode.Multiply,
                decalProjectOnMaids = true,
            };

            var text = Serialize(data.ToXml());

            Assert.DoesNotContain("DisplayType", text);
            Assert.DoesNotContain("Decal", text);
        }

        [Fact]
        public void 要素の無いMTEのXMLは板と既定値で読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>sample_png</ImageName><Group>0</Group>" +
                "<Primitive>0</Primitive><SquareUV>false</SquareUV><RenderQueue>3000</RenderQueue>" +
                "</TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal((int)PngDisplayType.Board, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Normal, restored.decalBlendMode);
            Assert.Equal(PngDecalProjection.DefaultFadeAngle, restored.decalFadeAngle);
            Assert.False(restored.decalProjectOnMaids);
            Assert.Equal(3000, restored.renderQueue);
        }

        [Fact]
        public void 範囲外の値は板と通常として読む()
        {
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>logo</ImageName>" +
                "<DisplayType>5</DisplayType><DecalBlendMode>-1</DecalBlendMode>" +
                "</TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal((int)PngDisplayType.Board, restored.displayType);
            Assert.Equal((int)PngDecalBlendMode.Normal, restored.decalBlendMode);
        }

        [Fact]
        public void 表示順の要素が無いXMLは0として読む()
        {
            // 0 以下は Setup で適用しない (描画順を壊さないため)。その前提となる読込値を固定する
            const string text =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<TimelinePngObjectXml><ImageName>logo</ImageName></TimelinePngObjectXml>";

            var restored = new TimelinePngObjectData();
            restored.FromXml(Deserialize(text));

            Assert.Equal(0, restored.renderQueue);
        }
    }
}
