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

        // 末尾まで来たら最後のタブを右端へ揃え、手前を見切れさせる
        [Fact]
        public void RightAlignsAtLastTab()
        {
            // active=7 (最終タブ) → firstVisible=4。
            // 4枚ぶんの幅 246 に対しタブ領域は 264 なので 18 余る
            var r = TabBarLayout.Calc(8, 300f, 0, 7);
            Assert.Equal(4, r.firstVisible);
            // 余りぶん右へ寄せ、左は手前の 1 枚を見切れさせて埋める
            Assert.Equal(3, r.firstDrawIndex);
            Assert.Equal(18f - (TabBarLayout.MIN_TAB_WIDTH + 2f), r.drawOriginX);
            Assert.Equal(7, r.lastDrawIndex);
        }

        // 途中までのスクロールでは左詰めのまま、末尾側だけ見切れさせる
        [Fact]
        public void DrawsOneExtraTabWhileScrolling()
        {
            var r = TabBarLayout.Calc(8, 300f, 1, -1);
            Assert.Equal(1, r.firstVisible);
            Assert.Equal(1, r.firstDrawIndex);
            Assert.Equal(0f, r.drawOriginX);
            Assert.Equal(5, r.lastDrawIndex);
        }

        [Fact]
        public void EmptyReturnsZeroVisible()
        {
            var r = TabBarLayout.Calc(0, 300f, 0, -1);
            Assert.Equal(0, r.visibleCount);
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
