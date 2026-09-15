using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 反転規則の現挙動を固定するテスト。
    /// Quaternion.Euler / eulerAngles は Unity ネイティブでテストプロセスから呼べないため、
    /// ここではオイラー角の算術だけを検証する（クォータニオン等価性の確認は実機で行う）
    /// </summary>
    public class PoseFlipUtilsTests
    {
        [Theory]
        [InlineData(IKManager.BoneType.TopFixed, true)]
        [InlineData(IKManager.BoneType.Mouth, true)]
        [InlineData(IKManager.BoneType.Nipple_R, true)]
        [InlineData(IKManager.BoneType.Hand_L, false)]
        [InlineData(IKManager.BoneType.Root, false)]
        public void IsNotFlipType_反転対象外の種別を判定する(IKManager.BoneType boneType, bool expected)
        {
            Assert.Equal(expected, PoseFlipUtils.IsNotFlipType(boneType));
        }

        [Theory]
        [InlineData(IKManager.BoneType.Hand_R, IKManager.BoneType.Hand_L)]
        [InlineData(IKManager.BoneType.Hand_L, IKManager.BoneType.Hand_R)]
        [InlineData(IKManager.BoneType.Bust_L, IKManager.BoneType.Bust_R)]
        [InlineData(IKManager.BoneType.Finger0_Root_L, IKManager.BoneType.Finger0_Root_R)]
        [InlineData(IKManager.BoneType.Toe0_Root_R, IKManager.BoneType.Toe0_Root_L)]
        // 左右の対が無い種別はそのまま
        [InlineData(IKManager.BoneType.Root, IKManager.BoneType.Root)]
        [InlineData(IKManager.BoneType.Pelvis, IKManager.BoneType.Pelvis)]
        public void GetFlippedBoneType_左右を入れ替える(
            IKManager.BoneType boneType, IKManager.BoneType expected)
        {
            Assert.Equal(expected, PoseFlipUtils.GetFlippedBoneType(boneType));
        }

        [Theory]
        // その他: X と Y を符号反転し Z は据え置き
        [InlineData(IKManager.BoneType.Hand_L, 10f, 20f, 30f, -10f, -20f, 30f)]
        [InlineData(IKManager.BoneType.Spine1, 10f, 20f, 30f, -10f, -20f, 30f)]
        // Root: y = 180 - (y - 180), z = 270 - (z - 270)
        [InlineData(IKManager.BoneType.Root, 10f, 20f, 30f, 10f, 340f, 510f)]
        // Pelvis: y += 180, z += 180
        [InlineData(IKManager.BoneType.Pelvis, 10f, 20f, 30f, 10f, 200f, 210f)]
        // Spine0: x = 270 - (x - 270)
        [InlineData(IKManager.BoneType.Spine0, 10f, 20f, 30f, 530f, 20f, 30f)]
        // Bust: y = 360 - (y - 180), z = 270 - (z - 270)
        [InlineData(IKManager.BoneType.Bust_L, 10f, 20f, 30f, 10f, 520f, 510f)]
        [InlineData(IKManager.BoneType.Bust_R, 10f, 20f, 30f, 10f, 520f, 510f)]
        public void FlipEulerAngles_種別ごとの反転規則を適用する(
            IKManager.BoneType flippedBoneType,
            float x, float y, float z,
            float expectedX, float expectedY, float expectedZ)
        {
            var actual = PoseFlipUtils.FlipEulerAngles(flippedBoneType, new Vector3(x, y, z));

            Assert.Equal(expectedX, actual.x, 3);
            Assert.Equal(expectedY, actual.y, 3);
            Assert.Equal(expectedZ, actual.z, 3);
        }

        [Fact]
        public void FlipRotation_XY平面の鏡像はXとYの符号を反転する()
        {
            var actual = PoseFlipUtils.FlipRotation(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));

            Assert.Equal(-0.1f, actual.x, 5);
            Assert.Equal(-0.2f, actual.y, 5);
            Assert.Equal(0.3f, actual.z, 5);
            Assert.Equal(0.9f, actual.w, 5);
        }

        [Fact]
        public void FlipRotation_2回適用すると元へ戻る()
        {
            var original = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            var actual = PoseFlipUtils.FlipRotation(PoseFlipUtils.FlipRotation(original));

            Assert.Equal(original.x, actual.x, 5);
            Assert.Equal(original.y, actual.y, 5);
            Assert.Equal(original.z, actual.z, 5);
            Assert.Equal(original.w, actual.w, 5);
        }

        [Theory]
        [InlineData(IKManager.BoneType.Root, true)]
        [InlineData(IKManager.BoneType.Pelvis, true)]
        [InlineData(IKManager.BoneType.Spine0, true)]
        [InlineData(IKManager.BoneType.Bust_L, true)]
        [InlineData(IKManager.BoneType.Bust_R, true)]
        [InlineData(IKManager.BoneType.Spine1, false)]
        [InlineData(IKManager.BoneType.Hand_L, false)]
        [InlineData(IKManager.BoneType.Finger0_Root_R, false)]
        public void HasEulerFlipRule_例外規則を持つ種別を判定する(
            IKManager.BoneType boneType, bool expected)
        {
            Assert.Equal(expected, PoseFlipUtils.HasEulerFlipRule(boneType));
        }
    }
}
