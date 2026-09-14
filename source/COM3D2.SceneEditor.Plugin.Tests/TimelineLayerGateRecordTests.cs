using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 「触ったレイヤー」の控え。値を書く直前に控え、編集の確定時に取り出して消す。
    /// Begin / End は GUIView を要するためここでは検証できない
    /// </summary>
    public class TimelineLayerGateRecordTests
    {
        [Fact]
        public void 控えたレイヤーを取り出せる()
        {
            TimelineLayerGate.RecordEditedLayer(typeof(LightTimelineLayer), 0);

            int slotNo;
            var layerType = TimelineLayerGate.TakeEditedLayer(out slotNo);

            Assert.Equal(typeof(LightTimelineLayer), layerType);
            Assert.Equal(0, slotNo);
        }

        [Fact]
        public void メイド単位レイヤーはスロット番号も控える()
        {
            TimelineLayerGate.RecordEditedLayer(typeof(MotionTimelineLayer), 3);

            int slotNo;
            var layerType = TimelineLayerGate.TakeEditedLayer(out slotNo);

            Assert.Equal(typeof(MotionTimelineLayer), layerType);
            Assert.Equal(3, slotNo);
        }

        [Fact]
        public void 取り出すと控えは消える()
        {
            TimelineLayerGate.RecordEditedLayer(typeof(LightTimelineLayer), 2);

            int slotNo;
            TimelineLayerGate.TakeEditedLayer(out slotNo);
            var second = TimelineLayerGate.TakeEditedLayer(out slotNo);

            // 消さないと、次に関係のない操作をしたときへ引きずってしまう
            Assert.Null(second);
            Assert.Equal(0, slotNo);
        }

        [Fact]
        public void ゲートの外で触ると控えが消える()
        {
            TimelineLayerGate.RecordEditedLayer(typeof(LightTimelineLayer), 0);

            TimelineLayerGate.RecordEditedLayerFromOpenGate();

            int slotNo;
            Assert.Null(TimelineLayerGate.TakeEditedLayer(out slotNo));
        }
    }
}
