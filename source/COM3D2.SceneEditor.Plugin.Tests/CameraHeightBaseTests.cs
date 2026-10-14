using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightBaseTests
    {
        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void 未設定のスロットの基準身長は0()
        {
            Assert.Equal(0, new TimelineData().GetCameraHeightBase(1));
        }

        [Fact]
        public void 基準身長はスロットごとに持つ()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            data.SetCameraHeightBase(1, 145);
            Assert.Equal(160, data.GetCameraHeightBase(0));
            Assert.Equal(145, data.GetCameraHeightBase(1));
        }

        [Fact]
        public void 身長が0以下なら設定しない()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            data.SetCameraHeightBase(0, 0);
            Assert.Equal(160, data.GetCameraHeightBase(0));
        }

        [Fact]
        public void 未設定のときだけ取り込む()
        {
            var data = new TimelineData();
            data.SetCameraHeightBaseIfUnset(0, 160);
            data.SetCameraHeightBaseIfUnset(0, 140);
            Assert.Equal(160, data.GetCameraHeightBase(0));
        }

        [Fact]
        public void 基準身長が空なら要素を書き出さない()
        {
            var text = Serialize(new TimelineData().ToXml());
            Assert.DoesNotContain("CameraHeightBases", text);
        }

        [Fact]
        public void 基準身長はXML往復で保持される()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(1, 145);
            data.SetCameraHeightBase(0, 160);
            var text = Serialize(data.ToXml());
            Assert.Contains("<CameraHeightBases>", text);

            var loaded = new TimelineData();
            loaded.FromXml(Deserialize(text));
            Assert.Equal(160, loaded.GetCameraHeightBase(0));
            Assert.Equal(145, loaded.GetCameraHeightBase(1));
        }

        [Fact]
        public void 要素の無いXMLは基準身長が空で読む()
        {
            var loaded = new TimelineData();
            loaded.SetCameraHeightBase(0, 150);
            loaded.FromXml(Deserialize(Serialize(new TimelineXml())));
            Assert.Equal(0, loaded.GetCameraHeightBase(0));
        }

        [Fact]
        public void 初版の要素を含むXMLも読める()
        {
            var text = Serialize(new TimelineXml()).Replace(
                "</TimelineData>",
                "<IsCameraHeightCorrection>true</IsCameraHeightCorrection>"
                + "<CameraHeightMaidSlotNo>0</CameraHeightMaidSlotNo>"
                + "<CameraHeightBase>160</CameraHeightBase></TimelineData>");
            Assert.Contains("<IsCameraHeightCorrection>", text);
            var loaded = new TimelineData();
            loaded.FromXml(Deserialize(text));
            Assert.Equal(0, loaded.GetCameraHeightBase(0));
        }

        [Fact]
        public void 基準身長はUndoの差分比較に含めない()
        {
            var data = new TimelineData();
            data.SetCameraHeightBase(0, 160);
            var xml = data.ToXml();
            Assert.DoesNotContain("CameraHeightBases", TimelineXmlDiff.SerializeWithoutLayers(xml));
            // 比較用に一時的に外すだけで、xml 自体からは消さない
            Assert.Single(xml.cameraHeightBases);
        }
    }
}
