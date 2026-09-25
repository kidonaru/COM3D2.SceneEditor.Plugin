using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineZoomMathTests
    {
        [Fact]
        public void 拡大で1段上がる() => Assert.Equal(13, TimelineZoomMath.StepFrameWidth(11, 1));

        [Fact]
        public void 縮小で1段下がる() => Assert.Equal(9, TimelineZoomMath.StepFrameWidth(11, -1));

        [Fact]
        public void 両端でクランプする()
        {
            Assert.Equal(40, TimelineZoomMath.StepFrameWidth(40, 1));
            Assert.Equal(1, TimelineZoomMath.StepFrameWidth(1, -1));
        }

        [Fact]
        public void 段にない値は最寄りの段から動かす()
        {
            // 10 は 9 と 11 の中間。近い方 (同距離なら小さい方の 9) を基準に 1 段上げて 11
            Assert.Equal(11, TimelineZoomMath.StepFrameWidth(10, 1));
        }

        [Fact]
        public void カーソル下のフレームを固定してスクロールを補正する()
        {
            // 幅 10 でスクロール 100・カーソル 50 → カーソル下は 15 フレーム目。幅 20 では 300 - 50 = 250
            Assert.Equal(250f, TimelineZoomMath.AnchorScrollX(100f, 50f, 10, 20), 3);
        }

        [Fact]
        public void スクロールは負にならない()
        {
            Assert.Equal(0f, TimelineZoomMath.AnchorScrollX(0f, 50f, 20, 10), 3);
        }

        [Fact]
        public void ラベル間隔は幅が狭いほど広がる()
        {
            Assert.Equal(5, TimelineZoomMath.LabelInterval(11, 5));   // 55px
            Assert.Equal(10, TimelineZoomMath.LabelInterval(6, 5));   // 60px
            Assert.Equal(30, TimelineZoomMath.LabelInterval(3, 5));   // 90px (10 だと 30px で不足)
            Assert.Equal(60, TimelineZoomMath.LabelInterval(1, 5));   // 60px
        }
    }
}
