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

        [Fact]
        public void GetFingerBoneNames_先端グループは手指の第3関節と足指の第2関節()
        {
            var names = MaidBoneGizmoController.GetFingerBoneNames(MaidBoneGizmoController.BoneGroup.Tip);

            Assert.Equal(16, names.Count);
            Assert.Contains("Bip01 R Finger02", names);
            Assert.Contains("Bip01 L Finger42", names);
            Assert.Contains("Bip01 R Toe01", names);
            Assert.Contains("Bip01 L Toe21", names);
        }

        [Fact]
        public void GetFingerBoneNames_中間グループは手指だけ()
        {
            var names = MaidBoneGizmoController.GetFingerBoneNames(MaidBoneGizmoController.BoneGroup.Mid);

            Assert.Equal(10, names.Count);
            Assert.Contains("Bip01 R Finger01", names);
            Assert.Contains("Bip01 L Finger41", names);
            Assert.DoesNotContain(names, name => name.Contains("Toe"));
        }

        [Fact]
        public void GetFingerBoneNames_根本グループは手指と足指の根本関節()
        {
            var names = MaidBoneGizmoController.GetFingerBoneNames(MaidBoneGizmoController.BoneGroup.Root);

            Assert.Equal(16, names.Count);
            Assert.Contains("Bip01 R Finger0", names);
            Assert.Contains("Bip01 L Finger4", names);
            Assert.Contains("Bip01 R Toe0", names);
            Assert.Contains("Bip01 L Toe2", names);
        }
    }
}
