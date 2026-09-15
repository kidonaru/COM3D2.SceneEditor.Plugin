using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>IK 固定項目はボーンメニュー上で対応する腕/脚グループに置く</summary>
    public class MaidIKHoldMenuGroupTests
    {
        [Theory]
        [InlineData(MaidIKHoldType.Arm_L_Joint, BoneSetMenuType.LeftArm)]
        [InlineData(MaidIKHoldType.Arm_L_Tip, BoneSetMenuType.LeftArm)]
        [InlineData(MaidIKHoldType.Arm_R_Joint, BoneSetMenuType.RightArm)]
        [InlineData(MaidIKHoldType.Arm_R_Tip, BoneSetMenuType.RightArm)]
        [InlineData(MaidIKHoldType.Foot_L_Joint, BoneSetMenuType.LeftLeg)]
        [InlineData(MaidIKHoldType.Foot_L_Tip, BoneSetMenuType.LeftLeg)]
        [InlineData(MaidIKHoldType.Foot_R_Joint, BoneSetMenuType.RightLeg)]
        [InlineData(MaidIKHoldType.Foot_R_Tip, BoneSetMenuType.RightLeg)]
        public void IK固定種別は対応する腕脚グループへ割り当てられる(MaidIKHoldType type, BoneSetMenuType expected)
        {
            Assert.Equal(expected, MaidIKHoldController.GetBoneSetMenuType(type));
        }

        [Fact]
        public void 未定義の種別はグループなしになる()
        {
            Assert.Equal(BoneSetMenuType.None, MaidIKHoldController.GetBoneSetMenuType(MaidIKHoldType.Max));
        }
    }
}
