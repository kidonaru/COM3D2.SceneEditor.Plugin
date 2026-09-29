using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>スライダー行の刻み (SliderOption.snapStep) の丸めとドラッグ量の積み上げを固定する</summary>
    public class SliderSnapTests
    {
        [Theory]
        [InlineData(102f, 100f)]
        [InlineData(103f, 105f)]
        [InlineData(50f, 80f)]
        [InlineData(250f, 200f)]
        public void 刻みへ丸めて範囲へ収める(float value, float expected)
        {
            Assert.Equal(expected, SliderSnap.Snap(value, 5f, 80f, 200f), 4);
        }

        [Fact]
        public void 刻み0以下は丸めない()
        {
            Assert.Equal(102.3f, SliderSnap.Snap(102.3f, 0f, 80f, 200f), 4);
        }

        [Fact]
        public void ドラッグ量は積み上げて刻みを越えた分だけ進む()
        {
            // 1 イベントの移動量が刻みより小さくても、積み上がれば 1 刻み進む
            var residual = 0f;
            Assert.Equal(0f, SliderSnap.TakeDragSteps(ref residual, 2f, 5f), 4);
            Assert.Equal(0f, SliderSnap.TakeDragSteps(ref residual, 2f, 5f), 4);
            Assert.Equal(5f, SliderSnap.TakeDragSteps(ref residual, 2f, 5f), 4);
            Assert.Equal(1f, residual, 4);
        }

        [Fact]
        public void 逆向きのドラッグは負の刻みで進む()
        {
            var residual = 0f;
            Assert.Equal(-10f, SliderSnap.TakeDragSteps(ref residual, -11f, 5f), 4);
            Assert.Equal(-1f, residual, 4);
        }
    }
}
