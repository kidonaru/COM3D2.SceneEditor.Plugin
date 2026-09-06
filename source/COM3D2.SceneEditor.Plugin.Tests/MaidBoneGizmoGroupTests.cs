using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidBoneGizmoGroupTests
    {
        [Fact]
        public void ResolveGroup_Alt非押下ならグループ無し()
        {
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, false, false));
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, true, false));
            Assert.Null(MaidBoneGizmoController.ResolveGroup(false, false, true));
        }

        [Theory]
        [InlineData(false, false, MaidBoneGizmoController.BoneGroup.Tip)]
        [InlineData(true, false, MaidBoneGizmoController.BoneGroup.Mid)]
        [InlineData(false, true, MaidBoneGizmoController.BoneGroup.Root)]
        // Ctrl と Shift を同時に押した場合は Ctrl を優先する (現行の判定順どおり)
        [InlineData(true, true, MaidBoneGizmoController.BoneGroup.Mid)]
        public void ResolveGroup_Alt押下中は修飾キーでグループが決まる(
            bool ctrl, bool shift, MaidBoneGizmoController.BoneGroup expected)
        {
            Assert.Equal(expected, MaidBoneGizmoController.ResolveGroup(true, ctrl, shift));
        }
    }
}
