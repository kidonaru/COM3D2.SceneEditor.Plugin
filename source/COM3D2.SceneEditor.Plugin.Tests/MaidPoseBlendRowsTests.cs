using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 適用先タブの見出しと、タブ位置とレイヤー番号の相互変換を固定する。
    /// 中身は選択中の 1 段しか出さないので、どこに載っているかはタブの目印だけが伝える
    /// </summary>
    public class MaidPoseBlendRowsTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 先頭はベースで載っている層に目印を付ける()
        {
            var result = MaidPoseBlendRows.BuildTargetLabels(Names, 2, 8);

            Assert.Equal(
                new List<string> { "ベース", "2*", "3", "4*", "5", "6", "7", "8" }, result);
        }

        [Fact]
        public void 何も載っていなければ番号だけ()
        {
            var result = MaidPoseBlendRows.BuildTargetLabels(new string[9], 2, 8);

            Assert.Equal(
                new List<string> { "ベース", "2", "3", "4", "5", "6", "7", "8" }, result);
        }

        [Fact]
        public void 配列が短くても全ての層を返す()
        {
            var result = MaidPoseBlendRows.BuildTargetLabels(new string[3] { "", "", "a.anm" }, 2, 8);

            Assert.Equal(
                new List<string> { "ベース", "2*", "3", "4", "5", "6", "7", "8" }, result);
        }

        [Fact]
        public void ベースはタブの先頭に対応する()
        {
            Assert.Equal(0, MaidPoseBlendRows.ToTabIndex(MaidPoseBlendRows.BaseLayer, 2));
            Assert.Equal(MaidPoseBlendRows.BaseLayer, MaidPoseBlendRows.ToTargetLayer(0, 2));
        }

        [Theory]
        [InlineData(2, 1)]
        [InlineData(5, 4)]
        [InlineData(8, 7)]
        public void レイヤー番号とタブ位置は往復する(int layer, int tabIndex)
        {
            Assert.Equal(tabIndex, MaidPoseBlendRows.ToTabIndex(layer, 2));
            Assert.Equal(layer, MaidPoseBlendRows.ToTargetLayer(tabIndex, 2));
        }
    }
}
