using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidShapeKeyRowDrawerTests
    {
        [Fact]
        public void IsEditable_未解決のシェイプキーは編集不可()
        {
            Assert.False(MaidShapeKeyRowDrawer.IsEditable(null));
        }

        [Fact]
        public void IsEditable_対象morphを持たないシェイプキーは編集不可()
        {
            // 着替えでスロットが外れると entities が空のまま返る
            Assert.False(MaidShapeKeyRowDrawer.IsEditable(new MTEP.MaidBlendShape()));
        }

        [Fact]
        public void IsEditable_対象morphが1つでもあれば編集可()
        {
            var blendShape = new MTEP.MaidBlendShape();
            blendShape.entities.Add(new MTEP.MaidBlendShape.Entity());
            Assert.True(MaidShapeKeyRowDrawer.IsEditable(blendShape));
        }
    }
}
