using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // 動画の履歴適用は ReloadMovie が重いため、パス・表示形式が変わったときだけ再読込する
    public class VideoReloadPolicyTests
    {
        private static ScenePresetVideo Make()
        {
            return new ScenePresetVideo { path = "a.mp4", enabled = true, displayType = 0, volume = 0.5f };
        }

        [Fact]
        public void 同一なら更新だけ()
        {
            Assert.Equal(VideoApplyAction.Update, VideoReloadPolicy.Decide(Make(), Make()));
        }

        [Fact]
        public void 音量など値だけの違いは更新だけ()
        {
            var after = Make();
            after.volume = 1f;
            Assert.Equal(VideoApplyAction.Update, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 有効フラグの違いは表示切替()
        {
            var after = Make();
            after.enabled = false;
            Assert.Equal(VideoApplyAction.Visible, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void パスの違いは再読込()
        {
            var after = Make();
            after.path = "b.mp4";
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 表示形式の違いは再読込()
        {
            var after = Make();
            after.displayType = 1;
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 比較元が無ければ再読込()
        {
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(null, Make()));
        }
    }
}
