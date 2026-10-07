using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// エフェクト表示 OFF のとき何を隠すか。編集中 (カレント) のレイヤーの種類だけは隠さないことを固定する
    /// </summary>
    public class ViewEffectVisibilityTests
    {
        [Theory]
        [InlineData(EffectLayerKind.None)]
        [InlineData(EffectLayerKind.PostEffect)]
        [InlineData(EffectLayerKind.StageLight)]
        public void 表示ONなら何も隠さない(EffectLayerKind current)
        {
            var state = ViewEffectVisibility.Resolve(true, current);

            Assert.False(state.suspendPostEffect);
            Assert.False(state.hideStageLight);
            Assert.False(state.hideStageLaser);
            Assert.False(state.hidePsyllium);
        }

        [Fact]
        public void 表示OFFでカレントがエフェクト以外なら全部隠す()
        {
            var state = ViewEffectVisibility.Resolve(false, EffectLayerKind.None);

            Assert.True(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }

        [Fact]
        public void 表示OFFでもカレントがポストエフェクトならポストエフェクトだけ出す()
        {
            var state = ViewEffectVisibility.Resolve(false, EffectLayerKind.PostEffect);

            Assert.False(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }

        [Theory]
        [InlineData(EffectLayerKind.StageLight, false, true, true)]
        [InlineData(EffectLayerKind.StageLaser, true, false, true)]
        [InlineData(EffectLayerKind.Psyllium, true, true, false)]
        public void 表示OFFでもカレントのライブ演出だけ出す(
            EffectLayerKind current, bool hideLight, bool hideLaser, bool hidePsyllium)
        {
            var state = ViewEffectVisibility.Resolve(false, current);

            Assert.True(state.suspendPostEffect);
            Assert.Equal(hideLight, state.hideStageLight);
            Assert.Equal(hideLaser, state.hideStageLaser);
            Assert.Equal(hidePsyllium, state.hidePsyllium);
        }

        [Fact]
        public void SceneView用はライブ演出だけを隠しポストエフェクトに触れない()
        {
            var state = ViewEffectVisibility.HideAll(true);

            Assert.False(state.suspendPostEffect);
            Assert.True(state.hideStageLight);
            Assert.True(state.hideStageLaser);
            Assert.True(state.hidePsyllium);
        }
    }
}
