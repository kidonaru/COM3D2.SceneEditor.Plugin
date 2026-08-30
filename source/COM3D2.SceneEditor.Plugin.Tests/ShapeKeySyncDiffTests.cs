using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// チェック集合 → タイムライン opt-in の片方向同期の差分計算を固定する。
    /// 差分が過不足なく出ないと、0F 自動キーやキー削除が漏れる/暴発する
    /// </summary>
    public class ShapeKeySyncDiffTests
    {
        private readonly List<string> _toAdd = new List<string>();
        private readonly List<string> _toRemove = new List<string>();

        private void Compute(string[] desired, string[] current)
        {
            ShapeKeySyncDiff.Compute(desired, new HashSet<string>(current), _toAdd, _toRemove);
        }

        [Fact]
        public void 増えた分だけ追加に出る()
        {
            Compute(new[] { "a", "b" }, new[] { "a" });

            Assert.Equal(new[] { "b" }, _toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 減った分だけ削除に出る()
        {
            Compute(new[] { "a" }, new[] { "a", "b" });

            Assert.Empty(_toAdd);
            Assert.Equal(new[] { "b" }, _toRemove);
        }

        [Fact]
        public void 同じなら差分は空()
        {
            Compute(new[] { "a", "b" }, new[] { "b", "a" });

            Assert.Empty(_toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 入れ替わりは追加と削除の両方に出る()
        {
            Compute(new[] { "a" }, new[] { "b" });

            Assert.Equal(new[] { "a" }, _toAdd);
            Assert.Equal(new[] { "b" }, _toRemove);
        }

        [Fact]
        public void 呼び出しごとに結果リストがクリアされる()
        {
            Compute(new[] { "a" }, new string[0]);
            Compute(new string[0], new string[0]);

            Assert.Empty(_toAdd);
            Assert.Empty(_toRemove);
        }

        [Fact]
        public void 空文字とnullは無視する()
        {
            ShapeKeySyncDiff.Compute(
                new[] { "a", "", null }, new HashSet<string>(), _toAdd, _toRemove);

            Assert.Equal(new[] { "a" }, _toAdd);
        }
    }
}
