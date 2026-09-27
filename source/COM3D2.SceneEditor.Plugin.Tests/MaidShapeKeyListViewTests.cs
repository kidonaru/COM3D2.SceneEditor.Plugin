using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェイプキー一覧の検索と並びを固定する (シェイプキーウィンドウと表情ウィンドウで共有)</summary>
    public class MaidShapeKeyListViewTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void 検索_未入力なら素通し(string searchText)
        {
            Assert.True(MaidShapeKeyListView.IsSearchMatched("anything", searchText));
        }

        [Fact]
        public void 検索は大文字小文字を区別しない()
        {
            Assert.True(MaidShapeKeyListView.IsSearchMatched("MuneUp", "muneup"));
            Assert.True(MaidShapeKeyListView.IsSearchMatched("muneup", "UP"));
        }

        [Fact]
        public void 検索_含まなければ外す()
        {
            Assert.False(MaidShapeKeyListView.IsSearchMatched("mune", "hoho"));
        }

        [Fact]
        public void タグ一覧_フィルタなしは全部を並べ替える()
        {
            var result = MaidShapeKeyListView.BuildTagList(new[] { "b", "c", "a" }, null);
            Assert.Equal(new List<string> { "a", "b", "c" }, result);
        }

        [Fact]
        public void タグ一覧_フィルタがfalseの名前は外す()
        {
            var result = MaidShapeKeyListView.BuildTagList(
                new[] { "eyeclose", "custom2", "custom1" }, name => name != "eyeclose");
            Assert.Equal(new List<string> { "custom1", "custom2" }, result);
        }

        [Fact]
        public void タグ一覧_元の列は書き換えない()
        {
            var source = new List<string> { "b", "a" };
            MaidShapeKeyListView.BuildTagList(source, null);
            Assert.Equal(new List<string> { "b", "a" }, source);
        }
    }
}
