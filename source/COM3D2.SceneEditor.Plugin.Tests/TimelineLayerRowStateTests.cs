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
        public void アクティブレイヤーは未トグルでも表示扱い()
        {
            Assert.True(_state.IsVisible("A", "A"));
            Assert.False(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void トグルで表示のオンオフが切り替わる()
        {
            _state.ToggleVisible("B", "A");
            Assert.True(_state.IsVisible("B", "A"));
            _state.ToggleVisible("B", "A");
            Assert.False(_state.IsVisible("B", "A"));
        }

        [Fact]
        public void アクティブレイヤーの非表示トグルは無視される()
        {
            _state.ToggleVisible("A", "A");
            Assert.True(_state.IsVisible("A", "A"));
        }

        [Fact]
        public void 折りたたみトグルが切り替わる()
        {
            Assert.False(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.True(_state.IsCollapsed("A"));
            _state.ToggleCollapsed("A");
            Assert.False(_state.IsCollapsed("A"));
        }

        [Fact]
        public void Pruneで死んだレイヤーが集合から消える()
        {
            _state.ToggleVisible("B", "A");
            _state.ToggleCollapsed("B");
            _state.Prune(new List<string> { "A" });
            Assert.False(_state.IsVisible("B", "A"));
            Assert.False(_state.IsCollapsed("B"));
        }

        [Fact]
        public void BuildRowsは表示レイヤーごとにカテゴリ行とアイテム行を積む()
        {
            _state.ToggleVisible("B", "A");
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
            _state.ToggleCollapsed("A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);

            Assert.Single(rows);
            Assert.True(rows[0].isHeader);
        }

        [Fact]
        public void BuildRowsは呼ぶたびに結果をクリアして詰め直す()
        {
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            _state.BuildRows(new List<string> { "A" }, "A", CollectItems, rows);
            Assert.Equal(3, rows.Count);
        }
    }
}
