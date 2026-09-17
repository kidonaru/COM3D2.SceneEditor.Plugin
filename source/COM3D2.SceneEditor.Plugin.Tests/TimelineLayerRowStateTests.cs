using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRowStateTests
    {
        // TLayer/TItem は参照型なら何でもよいのでテストでは string を使い、
        // レイヤー自身をそのままキーにする
        private readonly TimelineLayerRowState<string, string> _state
            = new TimelineLayerRowState<string, string>(layer => layer);

        private static void CollectItems(string layer, List<string> result)
        {
            result.Add(layer + ":item0");
            result.Add(layer + ":item1");
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
            _state.ToggleCollapsed("B");
            _state.Prune(new List<string> { "A" });
            // 展開状態が捨てられ、既定の折りたたみへ戻る
            Assert.True(_state.IsCollapsed("B"));
        }

        [Fact]
        public void 同じキーの別インスタンスへ状態が引き継がれる()
        {
            // Undo でタイムラインが作り直され、レイヤーが別インスタンスになる状況を模す
            var layer = new string("A".ToCharArray());
            var rebuiltLayer = new string("A".ToCharArray());
            Assert.False(ReferenceEquals(layer, rebuiltLayer));

            _state.ToggleCollapsed(layer);

            Assert.False(_state.IsCollapsed(rebuiltLayer));
        }

        [Fact]
        public void Prune後も同じキーなら状態が残る()
        {
            // Undo 後の Prune (キーキャッシュ貼り直し) で状態まで落とさないことを確認する
            _state.ToggleCollapsed("A");

            _state.Prune(new List<string> { new string("A".ToCharArray()), new string("B".ToCharArray()) });

            Assert.False(_state.IsCollapsed("A"));
        }

        [Fact]
        public void BuildRowsはレイヤーごとにカテゴリ行とアイテム行を積む()
        {
            _state.ToggleCollapsed("A");
            _state.ToggleCollapsed("B");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A", "B" }, CollectItems, rows);

            // A(ヘッダ+2行) + B(ヘッダ+2行)
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
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);

            Assert.Single(rows);
            Assert.True(rows[0].isHeader);
        }

        [Fact]
        public void SetAllCollapsedは渡したレイヤー全てを対象にする()
        {
            var layers = new List<string> { "A", "B" };
            _state.ToggleCollapsed("B");

            _state.SetAllCollapsed(layers, true);
            Assert.True(_state.IsCollapsed("A"));
            Assert.True(_state.IsCollapsed("B"));

            _state.SetAllCollapsed(layers, false);
            Assert.False(_state.IsCollapsed("A"));
            Assert.False(_state.IsCollapsed("B"));
        }

        [Fact]
        public void SetAllCollapsedは渡していないレイヤーに触れない()
        {
            _state.ToggleCollapsed("C");
            _state.SetAllCollapsed(new List<string> { "A", "B" }, true);
            Assert.False(_state.IsCollapsed("C"));
        }

        [Fact]
        public void AreAllCollapsedは全レイヤーが畳まれたときだけ真になる()
        {
            var layers = new List<string> { "A", "B" };

            // 既定は全て折りたたみ
            Assert.True(_state.AreAllCollapsed(layers));
            _state.ToggleCollapsed("A");
            Assert.False(_state.AreAllCollapsed(layers));
            _state.ToggleCollapsed("A");
            Assert.True(_state.AreAllCollapsed(layers));
        }

        [Fact]
        public void BuildRowsは呼ぶたびに結果をクリアして詰め直す()
        {
            _state.ToggleCollapsed("A");
            var rows = new List<LayerRow<string, string>>();
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);
            _state.BuildRows(new List<string> { "A" }, CollectItems, rows);
            Assert.Equal(3, rows.Count);
        }
    }
}
