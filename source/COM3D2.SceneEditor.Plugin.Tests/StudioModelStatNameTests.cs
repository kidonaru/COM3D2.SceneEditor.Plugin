using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// StudioModelStat の名前生成を固定する。
    /// タイムライン XML はモデルを name で照合するため、
    /// group 採番がプロバイダ側で変わっても name の作られ方が変わってはいけない
    /// </summary>
    public class StudioModelStatNameTests
    {
        private static MTEP.StudioModelStat CreateStat(int group)
        {
            var info = new MTEP.OfficialObjectInfo
            {
                type = MTEP.StudioModelType.Mod,
                label = "テスト家具",
                fileName = "test_furniture.menu",
            };
            return new MTEP.StudioModelStat(
                info, group, null, PhotoTransTargetObject.AttachPoint.Null, -1, null, "ModItemExplorer", true);
        }

        [Fact]
        public void group0ならサフィックスが付かない()
        {
            var stat = CreateStat(0);
            Assert.Equal("test_furniture.menu", stat.name);
            Assert.Equal("テスト家具", stat.displayName);
        }

        [Fact]
        public void group指定でサフィックスが付く()
        {
            var stat = CreateStat(2);
            Assert.Equal("test_furniture.menu (2)", stat.name);
            Assert.Equal("テスト家具 (2)", stat.displayName);
        }

        [Fact]
        public void SetGroupで名前が付け直される()
        {
            var stat = CreateStat(0);
            stat.SetGroup(3);
            Assert.Equal("test_furniture.menu (3)", stat.name);
            Assert.Equal("テスト家具 (3)", stat.displayName);
        }
    }
}
