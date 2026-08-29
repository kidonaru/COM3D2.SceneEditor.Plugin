using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TabBarLayoutTests
    {
        // 収まる枚数なら従来通り縮小のみでスクロールしない
        [Fact]
        public void FitsWithoutScroll()
        {
            // 3枚 * (90+2) は 300 に収まらないので縮小されるが MIN(60) は上回る
            var r = TabBarLayout.Calc(3, 300f, 0, 0);
            Assert.False(r.scrollable);
            Assert.Equal(0, r.firstVisible);
            Assert.Equal(3, r.visibleCount);
            Assert.True(r.tabWidth >= TabBarLayout.MIN_TAB_WIDTH);
            Assert.Equal(0f, r.tabsOriginX);
            // クリップは不要なので利用可能幅がそのままタブ領域になる
            Assert.Equal(300f, r.tabsAreaWidth);
        }

        // 下限を割る枚数ならスクロールモードへ入り、幅は MIN 固定
        [Fact]
        public void EntersScrollMode()
        {
            // 8枚 を 300px へ: (300 - 2*7)/8 = 35.75 < 60
            var r = TabBarLayout.Calc(8, 300f, 0, 0);
            Assert.True(r.scrollable);
            Assert.Equal(TabBarLayout.MIN_TAB_WIDTH, r.tabWidth);
            // タブ領域 = 300 - (16+2)*2 = 264 → floor((264+2)/62) = 4
            Assert.Equal(4, r.visibleCount);
            Assert.Equal(TabBarLayout.SCROLL_BUTTON_WIDTH + 2f, r.tabsOriginX);
            // 見切れ描画のクリップ幅は両端のボタンを除いた領域
            Assert.Equal(264f, r.tabsAreaWidth);
        }

        // scrollOffset は範囲へクランプされる
        [Fact]
        public void ClampsScrollOffset()
        {
            var r = TabBarLayout.Calc(8, 300f, 99, -1);
            Assert.Equal(4, r.firstVisible); // 8 - 4
            var r2 = TabBarLayout.Calc(8, 300f, -5, -1);
            Assert.Equal(0, r2.firstVisible);
        }

        // アクティブタブが見えるよう自動追従する
        [Fact]
        public void FollowsActiveTab()
        {
            // 右へ外れているケース: active=7, offset=0 → firstVisible = 7-4+1 = 4
            var r = TabBarLayout.Calc(8, 300f, 0, 7);
            Assert.Equal(4, r.firstVisible);
            // 左へ外れているケース: active=1, offset=4 → firstVisible = 1
            var r2 = TabBarLayout.Calc(8, 300f, 4, 1);
            Assert.Equal(1, r2.firstVisible);
        }

        [Fact]
        public void EmptyReturnsZeroVisible()
        {
            var r = TabBarLayout.Calc(0, 300f, 0, -1);
            Assert.Equal(0, r.visibleCount);
        }

        // ヘッダー幅→利用可能幅: フレーム*2 + 閉じる(20+2*2) + ロック(20+2) を除く
        [Fact]
        public void CalcAvailableWidthSubtractsFrameAndButtons()
        {
            Assert.Equal(400f - 4 * 2 - (20 + 2 * 2) - (20 + 2), TabBarLayout.CalcAvailableWidth(400f));
        }
    }
}
