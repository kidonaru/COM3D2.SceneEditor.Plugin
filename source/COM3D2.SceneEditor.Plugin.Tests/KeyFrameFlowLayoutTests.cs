using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class KeyFrameFlowLayoutTests
    {
        [Fact]
        public void 列数_ちょうど3列入る幅で3を返す()
        {
            // 120 + (4 + 120) * 2 = 368
            Assert.Equal(3, KeyFrameFlowLayout.GetColumnCount(368f, 120f, 4f));
        }

        [Fact]
        public void 列数_4列目に1px足りなければ3を返す()
        {
            // 4 列には 368 + 4 + 120 = 492 必要
            Assert.Equal(3, KeyFrameFlowLayout.GetColumnCount(491f, 120f, 4f));
        }

        [Fact]
        public void 列数_1列ぶんに満たない幅でも1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(10f, 120f, 4f));
        }

        [Fact]
        public void 列数_幅が0以下でも1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(0f, 120f, 4f));
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(-50f, 120f, 4f));
        }

        [Fact]
        public void 列数_要素幅が0以下なら1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(500f, 0f, 4f));
        }
    }
}
