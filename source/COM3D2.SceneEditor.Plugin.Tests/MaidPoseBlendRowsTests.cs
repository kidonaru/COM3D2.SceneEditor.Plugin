using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ブレンド区間に描く層の決定を固定する (空の層は適用先だけ見せる)</summary>
    public class MaidPoseBlendRowsTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 名前のある層だけを昇順で返す()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 0, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 空の適用先は行として含める()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 3, 2, 8);
            Assert.Equal(new List<int> { 2, 3, 4 }, result);
        }

        [Fact]
        public void 適用先が既に載っている層なら重複しない()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 4, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 適用先が通常なら層は増えない()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(new string[9], 0, 2, 8);
            Assert.Empty(result);
        }
    }
}
