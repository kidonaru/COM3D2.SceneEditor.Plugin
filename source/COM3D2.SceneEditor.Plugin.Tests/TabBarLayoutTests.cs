using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TabBarLayoutTests
    {
        // 8枚 * (60+2) - 2 = 494 がタブ列の総幅、タブ領域は 300 - (16+2)*2 = 264
        private const float SCROLL_CONTENT_WIDTH = 494f;
        private const float SCROLL_AREA_WIDTH = 264f;
        private const float STEP = TabBarLayout.MIN_TAB_WIDTH + 2f;

        // 収まる枚数なら従来通り縮小のみでスクロールしない
        [Fact]
        public void FitsWithoutScroll()
        {
            // 3枚 * (90+2) は 300 に収まらないので縮小されるが MIN(60) は上回る
            var r = TabBarLayout.Calc(3, 300f, 0f);
            Assert.False(r.scrollable);
            Assert.True(r.tabWidth >= TabBarLayout.MIN_TAB_WIDTH);
            Assert.Equal(0f, r.tabsOriginX);
            // クリップは不要なので利用可能幅がそのままタブ領域になる
            Assert.Equal(300f, r.tabsAreaWidth);
            Assert.Equal(0, r.firstDrawIndex);
            Assert.Equal(2, r.lastDrawIndex);
            Assert.Equal(0f, r.drawOriginX);
        }

        // 下限を割る枚数ならスクロールモードへ入り、幅は MIN 固定
        [Fact]
        public void EntersScrollMode()
        {
            // 8枚 を 300px へ: (300 - 2*7)/8 = 35.75 < 60
            var r = TabBarLayout.Calc(8, 300f, 0f);
            Assert.True(r.scrollable);
            Assert.Equal(TabBarLayout.MIN_TAB_WIDTH, r.tabWidth);
            Assert.Equal(TabBarLayout.SCROLL_BUTTON_WIDTH + 2f, r.tabsOriginX);
            Assert.Equal(SCROLL_AREA_WIDTH, r.tabsAreaWidth);
            Assert.Equal(SCROLL_CONTENT_WIDTH - SCROLL_AREA_WIDTH, r.maxScrollX);
        }

        // スクロールはタブ単位ではなく px 単位。端のタブは途中で切れる
        [Fact]
        public void ScrollsByPixels()
        {
            var r = TabBarLayout.Calc(8, 300f, 70f);
            Assert.Equal(70f, r.scrollX);
            // 70px 送ると 2 枚目 (index 1) が 8px 削られた状態で左端に来る
            Assert.Equal(1, r.firstDrawIndex);
            Assert.Equal(STEP - 70f, r.drawOriginX);
            // 右端は 70+264 = 334 に掛かる index 5 まで
            Assert.Equal(5, r.lastDrawIndex);
        }

        // スクロール位置は [0, maxScrollX] へクランプされる
        [Fact]
        public void ClampsScrollX()
        {
            var max = SCROLL_CONTENT_WIDTH - SCROLL_AREA_WIDTH;
            var r = TabBarLayout.Calc(8, 300f, 9999f);
            Assert.Equal(max, r.scrollX);
            // 右端まで送ると最後のタブが領域の右端へ揃う
            Assert.Equal(7, r.lastDrawIndex);
            Assert.Equal(3, r.firstDrawIndex);
            Assert.Equal(3 * STEP - max, r.drawOriginX);

            var r2 = TabBarLayout.Calc(8, 300f, -5f);
            Assert.Equal(0f, r2.scrollX);
            Assert.Equal(0, r2.firstDrawIndex);
        }

        [Fact]
        public void EmptyDrawsNothing()
        {
            var r = TabBarLayout.Calc(0, 300f, 0f);
            // ループが 1 周も回らないよう -1 を返す
            Assert.Equal(-1, r.lastDrawIndex);
        }

        // ヘッダー幅→利用可能幅: フレーム*2 + 閉じる(20+2*2) + ロック(20+2) を除く
        [Fact]
        public void CalcAvailableWidthSubtractsFrameAndButtons()
        {
            Assert.Equal(400f - 4 * 2 - (20 + 2 * 2) - (20 + 2), TabBarLayout.CalcAvailableWidth(400f));
        }
    }
}
