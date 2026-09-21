using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 停止中にブレンド層を有効のまま残すかの判定を固定する。
    /// 「レイヤータブ選択中」または「編集モード外」なら残す
    /// </summary>
    public class MaidBlendEditStateTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        public void レイヤー選択中か編集モード外なら層を残す(
            bool isLayerSelected, bool isEditMode, bool expected)
        {
            Assert.Equal(expected,
                MaidAnimationBlendController.ShouldKeepLayersWhileStopped(isLayerSelected, isEditMode));
        }

        [Fact]
        public void 既定はベース選択でボーン未編集()
        {
            var state = new MaidAnimationBlendController.BlendEditState();
            Assert.Equal(MaidPoseBlendRows.BaseLayer, state.selectedLayer);
            Assert.False(state.hasBoneEdit);
        }
    }
}
