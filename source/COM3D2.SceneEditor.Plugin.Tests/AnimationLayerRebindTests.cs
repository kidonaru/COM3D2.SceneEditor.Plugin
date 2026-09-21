using COM3D2.MotionTimelineEditor;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 段に記録したアニメ名と Animation 上の state 名の引き当てを固定する。
    /// ゲームは state 名をファイル名の小文字で持ち、Mod の anmName は絶対パスになる
    /// </summary>
    public class AnimationLayerRebindTests
    {
        [Theory]
        [InlineData("maidcho_oha2_ONCE_.anm", "maidcho_oha2_once_.anm", true)]
        [InlineData("jump_s.anm", "jump_s.anm", true)]
        [InlineData(@"C:\mod\dance\odori.anm", "odori.anm", true)]
        [InlineData("C:/mod/dance/odori.anm", "odori.anm", true)]
        [InlineData("jump_s.anm", "jump_l.anm", false)]
        public void ファイル名を大小無視で突き合わせる(string anmName, string stateName, bool expected)
        {
            Assert.Equal(expected,
                MaidAnimationBlendController.IsSameAnm(anmName, stateName));
        }

        [Theory]
        [InlineData(null, "jump_s.anm")]
        [InlineData("", "jump_s.anm")]
        [InlineData("jump_s.anm", null)]
        [InlineData("jump_s.anm", "")]
        public void 名前が無ければ引き当てない(string anmName, string stateName)
        {
            Assert.False(MaidAnimationBlendController.IsSameAnm(anmName, stateName));
        }

        /// <summary>
        /// ベースを差し替えると層の AnimationState は無効になるが、
        /// 載せている内容まで捨てると適用先タブから層が消えてしまう。
        /// state は Unity 型でテストから触れないため、残す側の値だけ固定する
        /// </summary>
        [Fact]
        public void ベース差し替えでは層の設定を残してstateだけ落とす()
        {
            var info = new AnimationLayerInfo(2)
            {
                anmName = @"C:\mod\dance\odori.anm",
                startTime = 1.5f,
                weight = 0.4f,
                speed = 0.8f,
                loop = false,
                overrideTime = true,
            };

            MaidAnimationBlendController.DetachStateKeepSettings(info);

            Assert.Equal(@"C:\mod\dance\odori.anm", info.anmName);
            Assert.Equal(1.5f, info.startTime);
            Assert.Equal(0.4f, info.weight);
            Assert.Equal(0.8f, info.speed);
            Assert.False(info.loop);
            Assert.True(info.overrideTime);
        }

        /// <summary>層が空なら残すものが無いので、そのまま空であること</summary>
        [Fact]
        public void 空の段は空のまま()
        {
            var info = new AnimationLayerInfo(3);

            MaidAnimationBlendController.DetachStateKeepSettings(info);

            Assert.True(string.IsNullOrEmpty(info.anmName));
        }
    }
}
