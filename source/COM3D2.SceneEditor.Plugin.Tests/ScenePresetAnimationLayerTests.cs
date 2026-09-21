using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセットの層と履歴用 LayerState の相互変換を固定する</summary>
    public class ScenePresetAnimationLayerTests
    {
        [Fact]
        public void LayerStateから作り戻すと同じ値になる()
        {
            var source = new MaidAnimationBlendController.LayerState
            {
                layer = 3, anmName = "C:/mod/a.anm", time = 1.5f, weight = 0.4f, speed = 0.8f, loop = false,
                playing = true, overrideTime = true,
            };

            var preset = ScenePresetAnimationLayer.FromLayerState(source);
            var restored = preset.ToLayerState();

            Assert.Equal(3, restored.layer);
            Assert.Equal("C:/mod/a.anm", restored.anmName);
            Assert.Equal(1.5f, restored.time);
            Assert.Equal(0.4f, restored.weight);
            Assert.Equal(0.8f, restored.speed);
            Assert.False(restored.loop);
            // 再生中かはベースの再生状態から決まるので保存しない。時間上書きはタイムライン専用
            Assert.False(restored.playing);
            Assert.False(restored.overrideTime);
        }
    }
}
