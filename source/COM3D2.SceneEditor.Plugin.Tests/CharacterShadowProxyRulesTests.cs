using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CharacterShadowProxyRulesTests
    {
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);
        private const int ShadowMask = 1 << 3;

        private static int Mask(LightTargetMode mode, bool characterShadow)
        {
            return LightTarget.ToCullingMask(mode, characterShadow, CharacterMask, ShadowMask);
        }

        [Fact]
        public void 背景のみ_影あり_キャラの影ONのライトは複製を要する()
        {
            Assert.True(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(LightTargetMode.Background, true), CharacterMask, ShadowMask));
            Assert.True(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Hard, Mask(LightTargetMode.Background, true), CharacterMask, ShadowMask));
        }

        [Theory]
        [InlineData(LightTargetMode.All)]
        [InlineData(LightTargetMode.Character)]
        public void 背景のみ以外は複製を要さない(LightTargetMode mode)
        {
            // 影ビットは残っているが、キャラ本体が影を落とすので複製は要らない
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(mode, true), CharacterMask, ShadowMask));
        }

        [Fact]
        public void 無効_影なし_キャラの影OFF_影用レイヤーなしは複製を要さない()
        {
            var mask = Mask(LightTargetMode.Background, true);
            Assert.False(CharacterShadowProxyRules.IsCasterLight(false, LightShadows.Soft, mask, CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(true, LightShadows.None, mask, CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, Mask(LightTargetMode.Background, false), CharacterMask, ShadowMask));
            Assert.False(CharacterShadowProxyRules.IsCasterLight(
                true, LightShadows.Soft, LightTarget.ToCullingMask(LightTargetMode.Background, true, CharacterMask, 0),
                CharacterMask, 0));
        }

        [Theory]
        [InlineData(10, ShadowCastingMode.On, true)]
        [InlineData(11, ShadowCastingMode.TwoSided, true)]
        [InlineData(12, ShadowCastingMode.ShadowsOnly, true)]
        [InlineData(10, ShadowCastingMode.Off, false)]
        [InlineData(0, ShadowCastingMode.On, false)]
        [InlineData(20, ShadowCastingMode.On, false)]
        public void キャラ用レイヤーで影を落とす部位だけ複製する(int layer, ShadowCastingMode mode, bool expected)
        {
            Assert.Equal(expected, CharacterShadowProxyRules.ShouldProxy(layer, mode, CharacterMask));
        }

        [Fact]
        public void 影用レイヤーを描かないカメラでは判定しない()
        {
            Assert.True(CharacterShadowProxyRules.ShouldUseCamera(-1, ShadowMask));
            Assert.False(CharacterShadowProxyRules.ShouldUseCamera(1 << 8, ShadowMask));
            Assert.False(CharacterShadowProxyRules.ShouldUseCamera(-1, 0));
        }

        [Fact]
        public void 元が描かれるなら複製も影を落とす()
        {
            Assert.False(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 10, -1));
        }

        [Fact]
        public void 元が破棄_無効_非アクティブなら複製を止める()
        {
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(false, false, false, 0, -1));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, false, true, 10, -1));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, false, 10, -1));
        }

        [Fact]
        public void 元のレイヤーをカメラが描かないなら複製を止める()
        {
            // PostEffects のメイド非表示は Charactor / Face をカメラの cullingMask から外す
            var cameraMask = ~((1 << 10) | (1 << 11));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 10, cameraMask));
            Assert.True(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 11, cameraMask));
            Assert.False(CharacterShadowProxyRules.ShouldHideForCamera(true, true, true, 12, cameraMask));
        }
    }
}
