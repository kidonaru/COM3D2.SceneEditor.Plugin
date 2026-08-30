using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SubCameraRowDrawerTests
    {
        // 選択肢は「なし」+ メイド 3 人 = 4 件を想定する
        private const int ItemCount = 4;

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 1)]
        [InlineData(2, 3)]
        public void ToFollowMaidIndex_先頭のなし分だけずらす(int maidSlotNo, int expected)
        {
            Assert.Equal(expected, SubCameraRowDrawer.ToFollowMaidIndex(maidSlotNo, ItemCount));
        }

        [Fact]
        public void ToFollowMaidIndex_選択肢を超えるスロット番号は末尾へ丸める()
        {
            // 追従中のメイドが退去すると、キーの maidSlotNo だけが残る
            Assert.Equal(ItemCount - 1, SubCameraRowDrawer.ToFollowMaidIndex(9, ItemCount));
        }

        [Fact]
        public void ToFollowMaidIndex_選択肢がなしだけでも範囲内に収める()
        {
            Assert.Equal(0, SubCameraRowDrawer.ToFollowMaidIndex(3, 1));
        }

        [Theory]
        [InlineData(0, -1)]
        [InlineData(1, 0)]
        [InlineData(3, 2)]
        public void ToFollowMaidSlotNo_なしは追従解除の_1になる(int index, int expected)
        {
            Assert.Equal(expected, SubCameraRowDrawer.ToFollowMaidSlotNo(index));
        }
    }
}
