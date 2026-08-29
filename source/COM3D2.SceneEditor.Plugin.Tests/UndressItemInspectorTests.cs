using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class UndressItemInspectorTests
    {
        [Theory]
        [InlineData("wear", MTEP.DressSlotID.wear)]
        [InlineData("skirt", MTEP.DressSlotID.skirt)]
        [InlineData("undressfront", MTEP.DressSlotID.undressfront)]
        public void TryResolveSlotId_項目名からスロットIDへ戻せる(
            string itemName, MTEP.DressSlotID expected)
        {
            MTEP.DressSlotID slotId;
            Assert.True(UndressItemInspector.TryResolveSlotId(itemName, out slotId));
            Assert.Equal(expected, slotId);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("NotASlot")]
        // 数値文字列は Enum.Parse を素通りしてしまうため、定義済み名だけを通すことを確かめる
        [InlineData("0")]
        public void TryResolveSlotId_解決できない項目名は失敗を返す(string itemName)
        {
            MTEP.DressSlotID slotId;
            Assert.False(UndressItemInspector.TryResolveSlotId(itemName, out slotId));
        }

        /// <summary>
        /// レイヤーのメニュー項目名は DressSlotID.ToString() のため、
        /// 全項目が解決できることを実データで確かめる
        /// </summary>
        [Fact]
        public void TryResolveSlotId_脱衣レイヤーの全スロット名を解決できる()
        {
            foreach (var slotName in MTEP.DressUtils.DressSlotNames)
            {
                MTEP.DressSlotID slotId;
                Assert.True(UndressItemInspector.TryResolveSlotId(slotName, out slotId),
                    slotName + " を解決できませんでした");
                Assert.Equal(slotName, slotId.ToString());
            }
        }
    }
}
