using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTE = COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class DressItemInspectorTests
    {
        [Theory]
        [InlineData("wear", MTE.MaidPartType.wear)]
        [InlineData("skirt", MTE.MaidPartType.skirt)]
        public void TryResolvePartType_項目名から部位へ戻せる(
            string itemName, MTE.MaidPartType expected)
        {
            MTE.MaidPartType partType;
            Assert.True(DressItemInspector.TryResolvePartType(itemName, out partType));
            Assert.Equal(expected, partType);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("NotAPart")]
        // null_mpn は「解決できなかった」印なので、名前として渡されても成功にしない
        [InlineData("null_mpn")]
        public void TryResolvePartType_解決できない項目名は失敗を返す(string itemName)
        {
            MTE.MaidPartType partType;
            Assert.False(DressItemInspector.TryResolvePartType(itemName, out partType));
        }

        /// <summary>
        /// レイヤーのメニュー項目名は MaidPartType.ToName() のため、
        /// 装備可能な全部位が解決できることを実データで確かめる
        /// </summary>
        [Fact]
        public void TryResolvePartType_装備可能な全部位を解決できる()
        {
            foreach (var expected in MTE.MaidPartUtils.equippableMaidPartTypes)
            {
                var itemName = MTE.MaidPartUtils.ToName(expected);

                MTE.MaidPartType partType;
                Assert.True(DressItemInspector.TryResolvePartType(itemName, out partType),
                    itemName + " を解決できませんでした");
                Assert.Equal(expected, partType);
            }
        }
    }
}
