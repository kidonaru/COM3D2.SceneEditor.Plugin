using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightCorrectionTests
    {
        [Theory]
        [InlineData(0f, 0f, 0f, 0f, 134)]
        [InlineData(50f, 50f, 50f, 50f, 165)]   // 1652.5 / 10
        [InlineData(1f, 0f, 0f, 0f, 134)]       // 134.4 は切り捨て
        [InlineData(0f, 0f, 19f, 0f, 134)]      // 134.95 も切り捨て
        [InlineData(100f, 100f, 100f, 100f, 199)]
        public void 身長はプロフィールと同じ式で求める(
            float sintyou, float douPer, float kubiScl, float headY, int expected)
        {
            Assert.Equal(expected, CameraHeightCorrection.CalcHeight(sintyou, douPer, kubiScl, headY));
        }

        [Fact]
        public void 基準と同じ身長なら倍率は1()
        {
            Assert.Equal(1f, CameraHeightCorrection.CalcScale(160, 160));
        }

        [Fact]
        public void 倍率は今の身長と基準身長の比()
        {
            Assert.Equal(0.9f, CameraHeightCorrection.CalcScale(160, 144), 4);
        }

        [Theory]
        [InlineData(0, 144)]
        [InlineData(-1, 144)]
        [InlineData(160, 0)]
        public void 補正できない条件では倍率1(int baseHeight, int currentHeight)
        {
            Assert.Equal(1f, CameraHeightCorrection.CalcScale(baseHeight, currentHeight));
        }

        [Fact]
        public void 補正はルートの高さを中心に拡縮する()
        {
            Assert.Equal(1.0f + (1.5f - 1.0f) * 0.8f, CameraHeightCorrection.Correct(1.5f, 1.0f, 0.8f), 5);
            Assert.Equal(1.0f, CameraHeightCorrection.Correct(1.0f, 1.0f, 0.8f), 5);
        }

        [Fact]
        public void 補間の端はそれぞれのメイドの補正に一致する()
        {
            var a = CameraHeightCorrection.Correct(1.5f, 0.2f, 0.8f);
            var b = CameraHeightCorrection.Correct(1.5f, -0.1f, 1.2f);
            Assert.Equal(a, CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, -0.1f, 1.2f, 0f), 5);
            Assert.Equal(b, CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, -0.1f, 1.2f, 1f), 5);
        }

        [Fact]
        public void 補正しない側は倍率1としてつながる()
        {
            var a = CameraHeightCorrection.Correct(1.5f, 0.2f, 0.8f);
            Assert.Equal(0.5f * a + 0.5f * 1.5f,
                CameraHeightCorrection.Blend(1.5f, 0.2f, 0.8f, 123f, 1f, 0.5f), 5);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(1f)]
        public void 全員の倍率が1なら補間しても動かない(float w)
        {
            Assert.Equal(1.5f, CameraHeightCorrection.Blend(1.5f, 0.2f, 1f, -0.4f, 1f, w), 5);
        }

        [Theory]
        [InlineData(1.4f, 0f, 0.9f, 0.5f, 1.1f, 0f)]
        [InlineData(1.4f, 0f, 0.9f, 0.5f, 1.1f, 0.35f)]
        [InlineData(-0.3f, 0.5f, 1.12f, -1f, 0.85f, 0.8f)]
        [InlineData(2.0f, -1f, 1f, 0.3f, 1.3f, 1f)]
        public void 補間と逆補間は往復で元に戻る(
            float y, float rootA, float scaleA, float rootB, float scaleB, float w)
        {
            Assert.Equal(y, CameraHeightCorrection.Unblend(
                CameraHeightCorrection.Blend(y, rootA, scaleA, rootB, scaleB, w),
                rootA, scaleA, rootB, scaleB, w), 4);
            Assert.Equal(y, CameraHeightCorrection.Blend(
                CameraHeightCorrection.Unblend(y, rootA, scaleA, rootB, scaleB, w),
                rootA, scaleA, rootB, scaleB, w), 4);
        }

        [Theory]
        [InlineData(1.5f, 0f, 0.9f)]
        [InlineData(1.5f, 0.3f, 1.1f)]   // ルートが床から浮いていても戻る
        [InlineData(0.2f, 0.5f, 0.8f)]   // 注視点がルートより下
        public void 逆補正して補正すると元に戻る(float y, float rootY, float scale)
        {
            var uncorrected = CameraHeightCorrection.Uncorrect(y, rootY, scale);

            Assert.Equal(y, CameraHeightCorrection.Correct(uncorrected, rootY, scale), 4);
        }

        [Fact]
        public void 基準身長が未設定なら逆補正しても値は変わらない()
        {
            var scale = CameraHeightCorrection.CalcScale(0, 150);

            Assert.Equal(1.5f, CameraHeightCorrection.Uncorrect(1.5f, 0.2f, scale), 4);
        }
    }
}
