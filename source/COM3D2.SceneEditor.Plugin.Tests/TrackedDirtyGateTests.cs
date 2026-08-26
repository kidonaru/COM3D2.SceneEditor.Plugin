using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 同期の門番の判定を固定する。
    /// 「要素の削除による version 合計の減少と、別要素の増分が釣り合う」ケースを
    /// 世代で拾えることがこのクラスの存在理由なので、そこを重点的に押さえる
    /// </summary>
    public class TrackedDirtyGateTests
    {
        [Fact]
        public void 初回は変化ありと判定する()
        {
            var gate = new TrackedDirtyGate();

            Assert.True(gate.IsChanged(0));
        }

        [Fact]
        public void 同期後に同じ合計なら変化なし()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            Assert.False(gate.IsChanged(5));
        }

        [Fact]
        public void 合計が変われば変化あり()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            Assert.True(gate.IsChanged(6));
        }

        [Fact]
        public void Invalidateすれば合計が同じでも変化あり()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            gate.Invalidate();

            Assert.True(gate.IsChanged(5));
        }

        [Fact]
        public void 集合の入れ替わりで合計が釣り合っても見逃さない()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            // 要素が 1 つ消え (version 3 が減り)、別要素が 3 進んで合計が元に戻ったケース
            gate.Invalidate();

            Assert.True(gate.IsChanged(5));
        }

        [Fact]
        public void IsChangedは状態を変えない()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            Assert.True(gate.IsChanged(6));
            Assert.True(gate.IsChanged(6));
        }

        [Fact]
        public void Resetで初回状態へ戻る()
        {
            var gate = new TrackedDirtyGate();
            gate.MarkSynced(5);

            gate.Reset();

            Assert.True(gate.IsChanged(5));
        }
    }
}
