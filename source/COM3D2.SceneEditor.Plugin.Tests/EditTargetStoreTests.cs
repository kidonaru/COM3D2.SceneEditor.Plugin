using System.Linq;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // EditTargetStore はプリセット保存とタイムライン絞り込みの共通ソース。
    // version はタイムライン側の再構築検知に使うため「実際に集合が変わったときだけ」増える
    public class EditTargetStoreTests
    {
        [Fact]
        public void Markでチェック済みになる()
        {
            var store = new EditTargetStore();
            store.Mark("eyeclose");
            Assert.True(store.IsModified("eyeclose"));
            Assert.False(store.IsModified("eyeclose2"));
            Assert.False(store.isEmpty);
        }

        [Fact]
        public void Unmarkで解除される()
        {
            var store = new EditTargetStore();
            store.Mark("eyeclose");
            store.Unmark("eyeclose");
            Assert.False(store.IsModified("eyeclose"));
            Assert.True(store.isEmpty);
        }

        [Fact]
        public void SetNamesで丸ごと置き換わる()
        {
            var store = new EditTargetStore();
            store.Mark("eyeclose");
            store.SetNames(new[] { "mayuup", "mouthup" });
            Assert.False(store.IsModified("eyeclose"));
            Assert.True(store.IsModified("mayuup"));
            Assert.True(store.IsModified("mouthup"));
        }

        [Fact]
        public void 集合が変わったときだけversionが増える()
        {
            var store = new EditTargetStore();
            var v0 = store.version;

            store.Mark("eyeclose");
            var v1 = store.version;
            Assert.True(v1 > v0);

            // 既にチェック済みの Mark では増えない
            store.Mark("eyeclose");
            Assert.Equal(v1, store.version);

            // 未チェックの Unmark でも増えない
            store.Unmark("mayuup");
            Assert.Equal(v1, store.version);

            store.Unmark("eyeclose");
            Assert.True(store.version > v1);
        }

        [Fact]
        public void SetNamesは中身が同じならversionが増えない()
        {
            var store = new EditTargetStore();
            store.SetNames(new[] { "eyeclose", "mayuup" });
            var v1 = store.version;

            // 順序違いの同一集合では増えない (同じ表情への undo/redo・プリセット再適用)
            store.SetNames(new[] { "mayuup", "eyeclose" });
            Assert.Equal(v1, store.version);

            store.SetNames(new[] { "mayuup" });
            Assert.True(store.version > v1);
        }

        [Fact]
        public void null名と空文字は無視される()
        {
            var store = new EditTargetStore();
            store.Mark(null);
            store.Mark("");
            Assert.True(store.isEmpty);
            Assert.False(store.IsModified(null));
            store.SetNames(new[] { "eyeclose", null, "" });
            Assert.Single(store.GetNames());
        }

        [Fact]
        public void GetNamesはコピーを返す()
        {
            var store = new EditTargetStore();
            store.Mark("eyeclose");
            var names = store.GetNames();
            names.Clear();
            Assert.True(store.IsModified("eyeclose"));
        }

        [Fact]
        public void Clearで空になる()
        {
            var store = new EditTargetStore();
            store.Mark("eyeclose");
            var v1 = store.version;
            store.Clear();
            Assert.True(store.isEmpty);
            Assert.True(store.version > v1);

            // 空のときの Clear では増えない
            var v2 = store.version;
            store.Clear();
            Assert.Equal(v2, store.version);
        }
    }
}
