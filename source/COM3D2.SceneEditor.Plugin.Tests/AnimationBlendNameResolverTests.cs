using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド層に載せるアニメ名の決定を固定する。
    /// anmName はタイムライン (MaidCache.LoadAnimationLayer) が再ロードできる形で、
    /// state 名は CrossFadeLayerByFullPath がファイル名を使う仕様に合わせる
    /// </summary>
    public class AnimationBlendNameResolverTests
    {
        [Fact]
        public void state名はanmTagのファイル名部分になる()
        {
            Assert.Equal("name.anm", AnimationBlendNameResolver.GetStateTag(@"sub\name.anm"));
            Assert.Equal("name.anm", AnimationBlendNameResolver.GetStateTag(@"c:\mod\name.anm"));
            Assert.Equal("dance.anm", AnimationBlendNameResolver.GetStateTag("dance.anm"));
            // 小文字化はリゾルバ側で行う (呼び出し側は生の anmName を渡せる)
            Assert.Equal("name.anm", AnimationBlendNameResolver.GetStateTag(@"Sub\Name.ANM"));
            Assert.Equal("", AnimationBlendNameResolver.GetStateTag(""));
            Assert.Equal("", AnimationBlendNameResolver.GetStateTag(null));
        }

        [Fact]
        public void 公式モーションはdirect_fileをそのまま使う()
        {
            Assert.Equal("dance.anm", AnimationBlendNameResolver.ResolveMotion("dance.anm", false, false));
        }

        [Fact]
        public void 新ボディ男の公式モーションはcrc_を前置する()
        {
            Assert.Equal("crc_dance.anm", AnimationBlendNameResolver.ResolveMotion("dance.anm", false, true));
        }

        [Fact]
        public void Modモーションは絶対パスのままでcrc_を付けない()
        {
            Assert.Equal(@"c:\mod\x.anm", AnimationBlendNameResolver.ResolveMotion(@"c:\mod\x.anm", true, true));
        }

        [Fact]
        public void スクリプト経由はnullになる()
        {
            Assert.Null(AnimationBlendNameResolver.ResolveMotion("", false, false));
            Assert.Null(AnimationBlendNameResolver.ResolveMotion(null, false, false));
        }

        [Fact]
        public void マイポーズは相対パスに拡張子を足す()
        {
            Assert.Equal(@"sub\pose.anm", AnimationBlendNameResolver.ResolveMyPose(@"sub\pose"));
            Assert.Equal("pose.anm", AnimationBlendNameResolver.ResolveMyPose("pose"));
        }
    }
}
