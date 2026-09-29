using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>タブ並び替えの判定幅が UI 倍率の見た目と一致することを固定する</summary>
    public class TabReorderMathTests
    {
        [Fact]
        public void 倍率1は描画側のタブ幅とマージンの和()
        {
            var available = TabBarLayout.CalcAvailableWidth(600f);
            var layout = TabBarLayout.Calc(3, available, 0f);
            Assert.Equal(layout.tabWidth + TabBarDrawer.TAB_MARGIN, TabReorderMath.GetStep(600f, 3, 1f), 4);
        }

        [Fact]
        public void 並び替えの1枚ぶんの移動量は倍率を掛けた幅()
        {
            // 実幅 600 を 1.5 倍で描くと論理幅 400。利用可能幅 400 - 8 - 24 - 22 = 346、
            // 5 枚なら (346 - 2 × 4) / 5 = 67.6 (上限 90・下限 60 の間)。
            // 画面上の 1 枚ぶんは (67.6 + 2) × 1.5 = 104.4
            Assert.Equal(104.4f, TabReorderMath.GetStep(600f, 5, 1.5f), 3);
        }
    }
}
