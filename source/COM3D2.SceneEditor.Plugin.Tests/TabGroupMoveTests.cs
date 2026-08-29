using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TabGroupMoveTests
    {
        /// <summary>並びとタブ状態 push だけ検証できる最小フェイク</summary>
        private class FakeWindow : IDockableWindow
        {
            public int tabWindowId { get; set; }
            public string windowTitleForTab => "W" + tabWindowId;
            public TabGroup group { get; set; }
            public Rect windowRect { get; set; }
            public Rect headerRect => windowRect;
            public bool isShowWnd => true;
            public bool isTabVisible => group == null || group.activeWindow == this;
            public string[] lastTitles;
            public int lastActiveIndex = -1;
            public void NotifyTabVisibleChanged() { }
            public void SavePlacement() { }
            public void SetTabBarState(string[] titles, int activeIndex)
            {
                lastTitles = titles;
                lastActiveIndex = activeIndex;
            }
        }

        private static TabGroup MakeGroup(out List<FakeWindow> wins)
        {
            return MakeGroup(out wins, 3, 0f);
        }

        /// <summary>幅を持つグループ。スクロールが絡む検証用</summary>
        private static TabGroup MakeGroup(out List<FakeWindow> wins, int count, float width)
        {
            var group = new TabGroup();
            wins = new List<FakeWindow>();
            for (var i = 0; i < count; i++)
            {
                var w = new FakeWindow
                {
                    tabWindowId = i,
                    windowRect = new Rect(0f, 0f, width, 100f),
                };
                wins.Add(w);
                group.Add(w, activate: false);
            }
            return group;
        }

        [Fact]
        public void MoveReordersAndPushes()
        {
            var group = MakeGroup(out var wins);
            group.Move(wins[0], 2);
            Assert.Equal(new[] { wins[1], wins[2], wins[0] }, group.windows);
            // 並び替え後のタイトルが全員へ push されている
            Assert.Equal(new[] { "W1", "W2", "W0" }, wins[0].lastTitles);
        }

        [Fact]
        public void MoveKeepsActiveWindow()
        {
            var group = MakeGroup(out var wins);
            group.SetActive(wins[2]);
            group.Move(wins[2], 0);
            Assert.Same(wins[2], group.activeWindow);
            Assert.Equal(0, wins[0].lastActiveIndex);
        }

        // アクティブ窓が入れ替わったのに index が同じケース (先頭タブを閉じた場合) でも
        // 見切れたタブへの寄せが走る
        [Fact]
        public void PushScrollsWhenActiveWindowChangesAtSameIndex()
        {
            var group = MakeGroup(out var wins, 8, 300f);
            group.SetActive(wins[0]);
            // 右の方まで手動スクロールしてある状態にする
            group.tabScrollX = 200f;

            group.Remove(wins[0]);

            // 別の窓が同じ index 0 のアクティブになる。寄せを飛ばすと 200 のまま取り残される
            Assert.Same(wins[1], group.activeWindow);
            Assert.Equal(0f, group.tabScrollX);
        }

        [Fact]
        public void MoveIgnoresInvalid()
        {
            var group = MakeGroup(out var wins);
            var before = new List<IDockableWindow>(group.windows);
            group.Move(wins[0], 5);                       // 範囲外
            group.Move(new FakeWindow(), 1);              // 非メンバー
            group.Move(wins[1], 1);                       // 同位置
            Assert.Equal(before, group.windows);
        }
    }
}
