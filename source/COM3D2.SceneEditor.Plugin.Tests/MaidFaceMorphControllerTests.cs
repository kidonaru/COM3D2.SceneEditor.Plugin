using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MaidFaceMorphControllerTests
    {
        [Fact]
        public void FindDef_全カテゴリを横断してモーフ名で定義を引ける()
        {
            var eyeclose = MaidFaceMorphController.FindDef("eyeclose");
            Assert.NotNull(eyeclose);
            Assert.Equal("目閉じ", eyeclose.displayName);

            // オプションカテゴリ (トグル系) も引ける
            var hoho = MaidFaceMorphController.FindDef("hoho");
            Assert.NotNull(hoho);
            Assert.True(hoho.isToggle);
        }

        [Fact]
        public void FindDef_未知の名前はnull()
        {
            Assert.Null(MaidFaceMorphController.FindDef("unknown_morph"));
        }
    }
}
