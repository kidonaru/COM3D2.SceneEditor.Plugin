using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRowStateTests
    {
        // TLayer/TItem は参照型なら何でもよいのでテストでは string を使う
        private readonly TimelineLayerRowState<string, string> _state
            = new TimelineLayerRowState<string, string>();

        private static void CollectItems(string layer, List<string> result)
        {
            result.Add(layer + ":item0");
            result.Add(layer + ":item1");
        }

        [Fact]
        public void 未トグルのレイヤーは表示扱い()
        {
            Assert.True(_state.IsVisible("A", "A"));
            Assert.True(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void トグルで表示のオンオフが切り替わる()
        {
            _state.ToggleVisible("B", "A");
            Assert.False(_state.IsVisible("B", "A"));
            _state.ToggleVisible("B", "A");
            Assert.True(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void アクティブレイヤーの非表示トグルは無視される_既定表示のまま()
        {
            _state.ToggleVisible("A", "A");
            Assert.True(_state.IsVisible("A", "A"));
        }

        [Fact]
        public void 折りたたみトグルが切り替わる()
        {
            // 未トグルのレイヤーは折りたたみ状態から始まる
            Assert.True(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.False(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.True(_state.IsCollapsed("A"));
        }

        [Fact]
        public void Pruneで死んだレイヤーが集合から消える()
        {
            _state.ToggleVisible("B", "A");
            _state.ToggleCollapsed("B");
            _state.Prune(new List<string> { "A" });
            // 非表示指定も捨てられ、既定の表示へ戻る
            Assert.True(_state.IsVisible("B", "A"));
            // 展開状態も捨てられ、既定の折りたたみへ戻る
            Assert.True(_state.IsCollapsed("B"));
        }

        [Fact]
        public void BuildRowsは表示レイヤーごとにカテゴリ行とアイテム行を積む()
        {
            // C だけ非表示にする
            _state.ToggleVisible("C", "A");
            _state.ToggleCollapsed("A");
            _state.ToggleCollapsed("B");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A", "B", "C" }, "A", CollectItems, rows);

            // A(ヘッダ+2行) + B(ヘッダ+2行)。C は非表示
            Assert.Equal(6, rows.Count);
            Assert.True(rows[0].isHeader);
            Assert.Equal("A", rows[0].layer);
            Assert.Equal("A:item0", rows[1].menuItem);
            Assert.Equal("A:item1", rows[2].menuItem);
            Assert.True(rows[3].isHeader);
            Assert.Equal("B", rows[3].layer);
        }

        [Fact]
        public void 折りたたみ中はカテゴリ行だけ残る()
        {
            // 既定が折りたたみなのでトグルせずそのまま組み立てる
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);

            Assert.Single(rows);
            Assert.True(rows[0].isHeader);
        }

        [Fact]
        public void SetAllVisibleで全レイヤーの表示を一括で切り替える()
        {
            var layers = new List<string> { "A", "B", "C" };

            // 一括非表示でもアクティブレイヤーは残る
            _state.SetAllVisible(layers, false);
            Assert.True(_state.IsVisible("A", "A"));
            Assert.False(_state.IsVisible("B", "A"));
            Assert.False(_state.IsVisible("C", "A"));

            _state.SetAllVisible(layers, true);
            Assert.True(_state.IsVisible("B", "A"));
            Assert.True(_state.IsVisible("C", "A"));
        }

        [Fact]
        public void AreAllVisibleは全レイヤーが表示中のときだけ真になる()
        {
            var layers = new List<string> { "A", "B" };

            Assert.True(_state.AreAllVisible(layers, "A"));
            _state.ToggleVisible("B", "A");
            Assert.False(_state.AreAllVisible(layers, "A"));
        }

        [Fact]
        public void SetAllCollapsedは表示中のレイヤーだけを対象にする()
        {
            var layers = new List<string> { "A", "B" };
            // B を非表示にしたうえで展開しておき、一括操作の対象外であることを見る
            _state.ToggleVisible("B", "A");
            _state.ToggleCollapsed("B");

            _state.SetAllCollapsed(layers, "A", true);
            Assert.True(_state.IsCollapsed("A"));
            Assert.False(_state.IsCollapsed("B"));

            _state.SetAllCollapsed(layers, "A", false);
            Assert.False(_state.IsCollapsed("A"));
        }

        [Fact]
        public void AreAllCollapsedは表示中のレイヤーが全て畳まれたときだけ真になる()
        {
            var layers = new List<string> { "A", "B" };

            // 既定は全て折りたたみ
            Assert.True(_state.AreAllCollapsed(layers, "A"));
            _state.ToggleCollapsed("A");
            Assert.False(_state.AreAllCollapsed(layers, "A"));
            _state.ToggleCollapsed("A");
            Assert.True(_state.AreAllCollapsed(layers, "A"));
        }

        [Fact]
        public void 非表示レイヤーの展開はAreAllCollapsedに影響しない()
        {
            var layers = new List<string> { "A", "B" };

            // B は非表示なので、展開しても表示中は全て畳まれた扱い
            _state.ToggleVisible("B", "A");
            _state.ToggleCollapsed("B");
            Assert.True(_state.AreAllCollapsed(layers, "A"));
        }

        [Fact]
        public void BuildRowsは呼ぶたびに結果をクリアして詰め直す()
        {
            _state.ToggleCollapsed("A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            Assert.Equal(3, rows.Count);
        }
    }
}
