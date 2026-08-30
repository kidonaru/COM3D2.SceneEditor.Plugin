using System.IO;
using System.Xml.Serialization;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// Timeline.xml (タイムライン設定) の後方互換。
    /// SE 側にも同名の Config があるため、MTE 側は別名で参照する。
    /// 使われなくなった項目をクラスから消しても、既存の設定ファイルが
    /// 読めなくなったり他の項目が既定へ戻ったりしないことを固定する
    /// </summary>
    public class TimelineConfigXmlTests
    {
        [Fact]
        public void 削除済みの項目を含む設定ファイルも読み飛ばして復元できる()
        {
            // クラスから削除済みの項目。既存ユーザーの Timeline.xml には残っている
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<isAutoYureBone>false</isAutoYureBone>"
                + "<alwaysShowIK>true</alwaysShowIK>"
                + "<isIkBoxVisibleRoot>false</isIkBoxVisibleRoot>"
                + "<isIkBoxVisibleBody>false</isIkBoxVisibleBody>"
                + "<pluginEnabled>false</pluginEnabled>"
                + "<keyRepeatTime>0.5</keyRepeatTime>"
                + "<gridCellSize>2.5</gridCellSize>"
                + "<useHSVColor>true</useHSVColor>"
                + "<windowHoverColor><r>1</r><g>0</g><b>0</b><a>1</a></windowHoverColor>"
                + "<historyLimit>50</historyLimit>"
                + "<voiceMaxLength>12.5</voiceMaxLength>"
                + "<detailTransformCount>8</detailTransformCount>"
                + "</Config>";

            var serializer = new XmlSerializer(typeof(MTEP.Config));
            using (var reader = new StringReader(xml))
            {
                var config = (MTEP.Config) serializer.Deserialize(reader);

                Assert.Equal(12.5f, config.voiceMaxLength);
                Assert.Equal(8, config.detailTransformCount);
            }
        }
    }
}
