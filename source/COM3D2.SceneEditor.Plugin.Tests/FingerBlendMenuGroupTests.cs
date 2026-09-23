using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>指ブレンド項目はボーンメニュー上で対応する手指/足指グループに置く</summary>
    public class FingerBlendMenuGroupTests
    {
        [Theory]
        [InlineData(WindowPartsFingerBlend.Type.RightArm, BoneSetMenuType.RightArmFinger)]
        [InlineData(WindowPartsFingerBlend.Type.LeftArm, BoneSetMenuType.LeftArmFinger)]
        [InlineData(WindowPartsFingerBlend.Type.RightLeg, BoneSetMenuType.RightLegFinger)]
        [InlineData(WindowPartsFingerBlend.Type.LeftLeg, BoneSetMenuType.LeftLegFinger)]
        public void 指ブレンド種別は対応する指グループへ割り当てられる(
            WindowPartsFingerBlend.Type type, BoneSetMenuType expected)
        {
            Assert.Equal(expected, MotionTimelineLayer.GetFingerBlendSetMenuType(type));
        }

        [Fact]
        public void 全ての指ブレンドボーンがグループを持つ()
        {
            foreach (var type in MotionTimelineLayer.FingerBlendBoneTypeMap.Values)
            {
                Assert.NotEqual(BoneSetMenuType.None, MotionTimelineLayer.GetFingerBlendSetMenuType(type));
            }
        }
    }
}
