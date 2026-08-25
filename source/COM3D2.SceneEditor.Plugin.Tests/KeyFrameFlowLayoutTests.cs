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
        public void 列数_実際のフロー要素幅で幅367なら2列()
        {
            // フロー要素 1 個 = ラベル 70 + margin 5 + 入力欄 60 + リセット 20 = 155。
            // 幅 367 (Inspector 幅 400 から padding 20・インデント 8・margin 5 を引いた値) では
            // 2 列で 155 + 5 + 155 = 315 まで、3 列には 475 必要で入らない
            Assert.Equal(2, KeyFrameFlowLayout.GetColumnCount(367f, 155f, 5f));
        }

        [Fact]
        public void 列数_要素幅が0以下なら1を返す()
        {
            Assert.Equal(1, KeyFrameFlowLayout.GetColumnCount(500f, 0f, 4f));
        }
    }
}
