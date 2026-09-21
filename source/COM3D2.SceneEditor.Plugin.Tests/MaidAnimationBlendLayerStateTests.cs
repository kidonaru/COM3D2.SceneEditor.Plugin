using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド層スナップショットの同値判定を固定する。
    /// PoseSnapshot.Approximately から使い、変化なしの編集を履歴に積まないための判定
    /// </summary>
    public class MaidAnimationBlendLayerStateTests
    {
        private static MaidAnimationBlendController.LayerState Create(
            int layer = 2, string anmName = "a.anm", float time = 0.5f,
            float weight = 1f, float speed = 1f, bool loop = true, bool playing = false,
            bool overrideTime = false)
        {
            return new MaidAnimationBlendController.LayerState
            {
                layer = layer, anmName = anmName, time = time,
                weight = weight, speed = speed, loop = loop, playing = playing,
                overrideTime = overrideTime,
            };
        }

        [Fact]
        public void 同じ値は同値()
        {
            Assert.True(Create().Approximately(Create()));
        }

        [Fact]
        public void 微小な数値差は同値扱い()
        {
            Assert.True(Create(time: 0.5f).Approximately(Create(time: 0.5f + 1e-5f)));
        }

        [Fact]
        public void 再生中は時間の違いを無視する()
        {
            // 再生中の層は毎フレーム time が進むため、履歴の同値判定から外す
            Assert.True(Create(playing: true, time: 0.5f).Approximately(Create(playing: true, time: 3f)));
        }

        [Fact]
        public void 名前_ループ_再生中の違いは別物()
        {
            Assert.False(Create().Approximately(Create(anmName: "b.anm")));
            Assert.False(Create().Approximately(Create(loop: false)));
            Assert.False(Create().Approximately(Create(playing: true)));
            Assert.False(Create().Approximately(Create(overrideTime: true)));
        }

        [Fact]
        public void 重み_速度_停止中の時間の違いは別物()
        {
            Assert.False(Create().Approximately(Create(weight: 0.5f)));
            Assert.False(Create().Approximately(Create(speed: 0.5f)));
            Assert.False(Create().Approximately(Create(time: 1f)));
        }

        [Fact]
        public void リスト比較はnullと空を同値とし件数差は別物()
        {
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(null, new List<MaidAnimationBlendController.LayerState>()));
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(null, null));
            Assert.False(MaidAnimationBlendController.LayerStatesApproximately(
                new List<MaidAnimationBlendController.LayerState> { Create() }, null));
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(
                new List<MaidAnimationBlendController.LayerState> { Create() },
                new List<MaidAnimationBlendController.LayerState> { Create() }));
        }
    }
}
