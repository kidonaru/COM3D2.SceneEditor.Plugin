using System.Collections.Generic;
using Xunit;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルボーンの修飾名生成を固定する。
    /// タイムライン側の候補名 (StudioModelManager.boneNames = ModelBone.name) と
    /// 同じ規則でなければ追跡集合が一切マッチしなくなる
    /// </summary>
    public class ModelBoneTrackedNamesTests
    {
        [Fact]
        public void モデル名とボーン名をスラッシュで連結する()
        {
            Assert.Equal(
                "test_furniture.menu/Bone_01",
                SE.ModelBoneTrackedNames.Qualify("test_furniture.menu", "Bone_01"));
        }

        [Fact]
        public void グループ付きモデル名でも規則は同じ()
        {
            Assert.Equal(
                "test_furniture.menu_2/Bone_01",
                SE.ModelBoneTrackedNames.Qualify("test_furniture.menu_2", "Bone_01"));
        }

        [Theory]
        [InlineData(null, "Bone_01")]
        [InlineData("", "Bone_01")]
        [InlineData("model", null)]
        [InlineData("model", "")]
        public void どちらかが空ならnullを返す(string modelName, string boneName)
        {
            Assert.Null(SE.ModelBoneTrackedNames.Qualify(modelName, boneName));
        }

        [Fact]
        public void Collectはエントリを修飾して追加する()
        {
            var entries = new List<SE.BoneEditEntry>
            {
                new SE.BoneEditEntry { boneName = "Bone_01" },
                new SE.BoneEditEntry { boneName = "Bone_02" },
            };
            var result = new List<string> { "既存" };

            SE.ModelBoneTrackedNames.Collect("model.menu", entries, result);

            Assert.Equal(
                new[] { "既存", "model.menu/Bone_01", "model.menu/Bone_02" },
                result);
        }

        [Fact]
        public void Collectはモデル名が空なら何も追加しない()
        {
            var entries = new List<SE.BoneEditEntry>
            {
                new SE.BoneEditEntry { boneName = "Bone_01" },
            };
            var result = new List<string>();

            SE.ModelBoneTrackedNames.Collect("", entries, result);

            Assert.Empty(result);
        }
    }
}
