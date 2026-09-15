using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 公式データに無いファイル名の種別判定を固定する。
    /// タイムライン XML は種別を保存せず name から引き直すため、
    /// ここの判定が崩れると外部プラグインの配置モデルが復元できなくなる
    /// </summary>
    public class StudioModelFallbackTypeTests
    {
        [Fact]
        public void asset_bgは背景オブジェクトとして扱う()
        {
            Assert.Equal(
                MTEP.StudioModelType.Asset,
                MTEP.StudioModelManager.ResolveFallbackType("kdnr_midnight_stage_prefab_toon.asset_bg"));
        }

        [Fact]
        public void 拡張子の大文字小文字は区別しない()
        {
            Assert.Equal(
                MTEP.StudioModelType.Asset,
                MTEP.StudioModelManager.ResolveFallbackType("Sample.Asset_BG"));
        }

        [Theory]
        [InlineData("test_furniture.menu")]
        [InlineData("")]
        [InlineData(null)]
        public void それ以外は従来どおりMODアイテムとして扱う(string fileName)
        {
            Assert.Equal(
                MTEP.StudioModelType.Mod,
                MTEP.StudioModelManager.ResolveFallbackType(fileName));
        }
    }
}
