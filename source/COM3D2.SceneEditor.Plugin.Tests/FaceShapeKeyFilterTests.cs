using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>FaceShapeKeyFilter.IsFaceMorphName が除外する名前の集合を固定する</summary>
    public class FaceShapeKeyFilterTests
    {
        [Theory]
        [InlineData("eyeclose")]
        [InlineData("eyeclose5")]
        [InlineData("mayuha")]
        [InlineData("moutha")]
        [InlineData("hitomis")]
        [InlineData("hoho")]
        [InlineData("toothoff")]
        public void 表情タブのモーフは除外する(string name)
        {
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Fact]
        public void 表情レイヤーに無くタブにだけあるモーフも除外する()
        {
            // nosefook はタイムラインの保存対象外だが、オプションタブで編集できる
            Assert.DoesNotContain("nosefook", MTEP.FaceMorphUtils.saveMorphNames);
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName("nosefook"));
        }

        [Theory]
        [InlineData("eyeclose1_normal")]
        [InlineData("eyeclose1_tare")]
        [InlineData("eyeclose2_tsuri")]
        [InlineData("eyeclose8_normal")]
        public void CRC顔のサフィックス付きも除外する(string name)
        {
            Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Theory]
        [InlineData("itome_normal")]
        [InlineData("custom_shapekey")]
        [InlineData("eyeclose_custom")]
        public void タブで扱わない名前は残す(string name)
        {
            Assert.False(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void 空の名前は除外しない(string name)
        {
            Assert.False(FaceShapeKeyFilter.IsFaceMorphName(name));
        }

        [Fact]
        public void タブの全モーフを除外する()
        {
            foreach (var name in MaidFaceMorphController.GetAllMorphNames())
            {
                Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name), name);
            }
            foreach (var name in MTEP.FaceMorphUtils.saveMorphNames)
            {
                Assert.True(FaceShapeKeyFilter.IsFaceMorphName(name), name);
            }
        }
    }
}
