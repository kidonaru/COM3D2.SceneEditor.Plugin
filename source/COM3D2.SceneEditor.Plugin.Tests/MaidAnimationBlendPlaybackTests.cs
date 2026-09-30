using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 層を単独で流せる条件と、履歴の復元で層へ戻す再生状態を固定する。
    /// ベースを止めたまま層だけ流す操作を、復元が止めてしまわないためのもの
    /// </summary>
    public class MaidAnimationBlendPlaybackTests
    {
        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void ベース再生中か層を残す停止中なら層を流せる(
            bool basePlaying, bool keep, bool expected)
        {
            Assert.Equal(expected, MaidAnimationBlendController.CanPlayLayer(basePlaying, keep));
        }

        [Fact]
        public void ベース再生中は流れていた層を控えた速度で戻す()
        {
            bool enabled;
            float speed;
            MaidAnimationBlendController.GetRestoredPlayback(true, false, true, 0.5f, out enabled, out speed);

            Assert.True(enabled);
            Assert.Equal(0.5f, speed);
        }

        [Fact]
        public void 止めていた層は有効なまま速度0で戻す()
        {
            bool enabled;
            float speed;
            MaidAnimationBlendController.GetRestoredPlayback(true, false, false, 0.5f, out enabled, out speed);

            Assert.True(enabled);
            Assert.Equal(0f, speed);
        }

        [Fact]
        public void ベース停止中でも層を残す条件なら流れていた層を流したまま戻す()
        {
            bool enabled;
            float speed;
            MaidAnimationBlendController.GetRestoredPlayback(false, true, true, 1.5f, out enabled, out speed);

            Assert.True(enabled);
            Assert.Equal(1.5f, speed);
        }

        [Fact]
        public void 層を残さない停止中は層を無効にする()
        {
            bool enabled;
            float speed;
            MaidAnimationBlendController.GetRestoredPlayback(false, false, true, 1f, out enabled, out speed);

            Assert.False(enabled);
            Assert.Equal(0f, speed);
        }
    }
}
