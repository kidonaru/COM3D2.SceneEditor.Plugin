using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// メイド拡張ボーン名 ("{slotName}/{boneName}") の生成を固定する。
    /// MTE 側 (ExtendBoneCache.AddEntity) と同じ規則でなければ
    /// 追跡集合がタイムラインの候補名と一切マッチしなくなる
    /// </summary>
    public class SlotQualifiedNamesTests
    {
        [Fact]
        public void スロット名とボーン名をスラッシュで連結する()
        {
            Assert.Equal("skirt/Skirt_01", SlotQualifiedNames.Qualify("skirt", "Skirt_01"));
        }

        [Theory]
        [InlineData(null, "Skirt_01")]
        [InlineData("skirt", null)]
        [InlineData("", "Skirt_01")]
        [InlineData("skirt", "")]
        public void 名前が欠けていればnullを返す(string slotName, string boneName)
        {
            Assert.Null(SlotQualifiedNames.Qualify(slotName, boneName));
        }

        [Fact]
        public void Collectはエントリを修飾名にして積む()
        {
            var entries = new List<BoneEditEntry>
            {
                new BoneEditEntry { slotName = "skirt", boneName = "Skirt_01" },
                new BoneEditEntry { slotName = "wear", boneName = "Wear_02" },
            };

            var result = new List<string>();
            SlotQualifiedNames.Collect(entries, result);

            Assert.Equal(new[] { "skirt/Skirt_01", "wear/Wear_02" }, result);
        }

        [Fact]
        public void Collectはresultをクリアしない()
        {
            var result = new List<string> { "既存" };
            SlotQualifiedNames.Collect(
                new List<BoneEditEntry> { new BoneEditEntry { slotName = "skirt", boneName = "S" } },
                result);

            Assert.Equal(new[] { "既存", "skirt/S" }, result);
        }

        [Fact]
        public void Collectはnullを渡しても例外にならない()
        {
            SlotQualifiedNames.Collect(null, new List<string>());
            SlotQualifiedNames.Collect(new List<BoneEditEntry>(), null);
        }
    }
}
