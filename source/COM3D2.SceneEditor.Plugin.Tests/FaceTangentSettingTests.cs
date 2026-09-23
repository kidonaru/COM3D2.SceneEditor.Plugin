using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 表情レイヤーのタンジェント補間フラグの既定値と XML 往復を固定する。
    /// 旧 XML には要素が無いので OFF、新規作成は ON でなければならない
    /// </summary>
    public class FaceTangentSettingTests
    {
        [Fact]
        public void 新規タイムラインは表情タンジェントONである()
        {
            Assert.True(new TimelineData().isTangentFace);
        }

        [Fact]
        public void 要素の無い旧XMLを読むとOFFになる()
        {
            var xml = new TimelineXml();
            Assert.False(xml.isTangentFace);

            var data = new TimelineData();
            data.FromXml(xml);
            Assert.False(data.isTangentFace);
        }

        [Fact]
        public void 表情タンジェントはXML往復で保持される()
        {
            var data = new TimelineData { isTangentFace = true };
            var xml = data.ToXml();
            Assert.True(xml.isTangentFace);

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TimelineXml));
            TimelineXml dst;
            using (var ms = new System.IO.MemoryStream())
            {
                serializer.Serialize(ms, xml);
                ms.Position = 0;
                dst = (TimelineXml)serializer.Deserialize(ms);
            }
            Assert.True(dst.isTangentFace);

            var loaded = new TimelineData();
            loaded.FromXml(dst);
            Assert.True(loaded.isTangentFace);
        }
    }
}
