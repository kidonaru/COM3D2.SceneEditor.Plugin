using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 強制上書きキーの値変換と実効値の決定を固定する。
    /// レイヤー本体は Unity 依存で単体テストできないため、判定だけを純関数として切り出している
    /// </summary>
    public class FaceForceOverrideTests
    {
        [Theory]
        [InlineData(0f, false)]
        [InlineData(0.49f, false)]
        [InlineData(0.5f, true)]
        [InlineData(1f, true)]
        public void 値はしきい値05でONOFFに変換される(float value, bool expected)
        {
            Assert.Equal(expected, FaceMorphUtils.ToForceOverride(value));
        }

        [Fact]
        public void ONは1でOFFは0へ変換される()
        {
            Assert.Equal(1f, FaceMorphUtils.ToForceOverrideValue(true));
            Assert.Equal(0f, FaceMorphUtils.ToForceOverrideValue(false));
        }

        [Fact]
        public void 抑止中は退避値によらず実効値がONになる()
        {
            Assert.True(FaceMorphUtils.ResolveForceOverride(true, true));
            Assert.True(FaceMorphUtils.ResolveForceOverride(true, false));
        }

        [Fact]
        public void 非抑止中はまばたきの反転が実効値になる()
        {
            Assert.False(FaceMorphUtils.ResolveForceOverride(false, true));
            Assert.True(FaceMorphUtils.ResolveForceOverride(false, false));
        }

        [Fact]
        public void 強制上書きキーの既定値はONである()
        {
            var trans = new TransformDataFaceSetting();
            trans.Initialize(FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME);

            Assert.Equal(TransformType.FaceSetting, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.True(FaceMorphUtils.ToForceOverride(trans.forceOverride));
        }
    }
}
