using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 読み手の無いスカラー値 (終了オフセット時間・フェード時間) と、GameView の設定へ移した
    /// 背景表示・廃止した地面色の背景連動を保存対象から外したことを固定する。
    /// 旧 XML に要素が残っていても XmlSerializer は未知要素を捨てるので、読込互換の残置は不要
    /// </summary>
    public class TimelineXmlRemovedFieldsTests
    {
        private static readonly string[] RemovedElements =
            { "EndOffsetTime", "StartFadeTime", "EndFadeTime", "IsBackgroundVisible", "IsGroundLinkedToBackground" };

        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, xml);
                return sw.ToString();
            }
        }

        [Fact]
        public void 廃止したスカラー値は出力されない()
        {
            var saved = XDocument.Parse(Serialize(new TimelineXml()));
            var names = saved.Descendants().Select(e => e.Name.LocalName).ToList();

            foreach (var removed in RemovedElements)
            {
                Assert.DoesNotContain(removed, names);
            }

            // 開始オフセット時間は BGM・動画のシークで使うので残す
            Assert.Contains("StartOffsetTime", names);
        }

        [Fact]
        public void 廃止したスカラー値を含む旧XMLも読み込める()
        {
            var source =
                "<TimelineData><StartOffsetTime>0.5</StartOffsetTime>" +
                "<EndOffsetTime>0.5</EndOffsetTime>" +
                "<StartFadeTime>0.1</StartFadeTime>" +
                "<EndFadeTime>0</EndFadeTime>" +
                "<IsBackgroundVisible>false</IsBackgroundVisible>" +
                "<IsGroundLinkedToBackground>true</IsGroundLinkedToBackground>" +
                "<IsLoopAnm>false</IsLoopAnm></TimelineData>";

            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var sr = new StringReader(source))
            {
                var xml = (TimelineXml)serializer.Deserialize(sr);
                Assert.Equal(0.5f, xml.startOffsetTime, 4);
                // 廃止した要素の後ろの要素まで読めている
                Assert.False(xml.isLoopAnm);
            }
        }
    }
}
