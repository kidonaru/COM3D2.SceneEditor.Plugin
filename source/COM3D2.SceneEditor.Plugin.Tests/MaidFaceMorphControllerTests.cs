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

        [Theory]
        [InlineData("eyeclose", 0, "eyeclose1_normal")]
        [InlineData("eyeclose", 1, "eyeclose1_tare")]
        [InlineData("eyeclose", 2, "eyeclose1_tsuri")]
        [InlineData("eyeclose2", 0, "eyeclose2_normal")]
        [InlineData("itome", 2, "itome_tsuri")]
        public void GetCrcMorphName_目型のサフィックスを付ける(
            string name, int faceTypeIndex, string expected)
        {
            Assert.Equal(expected, MaidFaceMorphController.GetCrcMorphName(name, faceTypeIndex));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(99)]
        public void GetCrcMorphName_想定外の目型でも配列外参照にならない(int faceTypeIndex)
        {
            // GP01FB_FACE_TYPE.MAX などが渡っても落ちず、両端へ丸める
            var actual = MaidFaceMorphController.GetCrcMorphName("eyeclose2", faceTypeIndex);
            Assert.StartsWith("eyeclose2_", actual);
        }

        [Theory]
        [InlineData("eyeclose3", true)]
        [InlineData("eyeclose", false)]
        [InlineData("eyeclose2", false)]
        [InlineData("itome", false)]
        public void IsTripleRangeMorph_値域が3倍なのはジト目だけ(string name, bool expected)
        {
            Assert.Equal(expected, MaidFaceMorphController.IsTripleRangeMorph(name));
        }
    }
}
