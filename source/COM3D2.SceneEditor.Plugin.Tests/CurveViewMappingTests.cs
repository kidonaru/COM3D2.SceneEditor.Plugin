using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CurveViewMappingTests
    {
        [Fact]
        public void 値からY座標へ_上端が最大値()
        {
            // paneHeight=100, 値域 0..10 → 値10 は Y=0(上端)、値0 は Y=100(下端)
            var m = new CurveViewMapping(frameWidth: 11f, paneHeight: 100f, valueMin: 0f, valueMax: 10f);
            Assert.Equal(0f, m.ValueToY(10f), 3);
            Assert.Equal(100f, m.ValueToY(0f), 3);
            Assert.Equal(50f, m.ValueToY(5f), 3);
        }

        [Fact]
        public void Y座標から値へ_往復が一致()
        {
            var m = new CurveViewMapping(11f, 100f, -2f, 8f);
            Assert.Equal(3.5f, m.YToValue(m.ValueToY(3.5f)), 3);
        }

        [Fact]
        public void フレームからX座標へ_フレーム中心()
        {
            var m = new CurveViewMapping(10f, 100f, 0f, 1f);
            // ドープシートのキー描画に合わせフレーム中心 (frameNo * frameWidth + frameWidth/2)
            Assert.Equal(5f, m.FrameToX(0), 3);
            Assert.Equal(35f, m.FrameToX(3), 3);
        }

        [Fact]
        public void スクリーン勾配から値勾配へ()
        {
            // frameWidth=10px, 値域 0..10 を 100px → 1 値 = 10px
            // 画面上 右10px・上10px の勾配 = 1フレームあたり値 +1
            var m = new CurveViewMapping(10f, 100f, 0f, 10f);
            Assert.Equal(1f, m.ScreenSlopeToValueSlope(10f, -10f), 3);
        }

        [Fact]
        public void AutoFit_余白付きで値域を決める()
        {
            var m = CurveViewMapping.AutoFit(11f, 100f, new[] { 0f, 10f });
            // 上下 10% 余白
            Assert.Equal(-1f, m.valueMin, 3);
            Assert.Equal(11f, m.valueMax, 3);
        }

        [Fact]
        public void AutoFit_値域ゼロでも潰れない()
        {
            var m = CurveViewMapping.AutoFit(11f, 100f, new[] { 5f, 5f });
            Assert.True(m.valueMax > m.valueMin);
            Assert.Equal(50f, m.ValueToY(5f), 3);
        }
    }
}
