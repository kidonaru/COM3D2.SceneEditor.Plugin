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

        [Fact]
        public void TryAdjustClosedEyeValues_合計1以内なら補正しない()
        {
            var values = new MaidFaceMorphController.ClosedEyeMorphValues
            {
                close = 0.5f,
                close2 = 0.5f,
            };

            MaidFaceMorphController.ClosedEyeMorphValues result;
            Assert.False(
                MaidFaceMorphController.TryAdjustClosedEyeValues(values, out result));
        }

        [Fact]
        public void TryAdjustClosedEyeValues_片目のウィンク2種を合計1へ収める()
        {
            // 小さい方 (winkL1) が残り幅へ切り詰められる
            var values = new MaidFaceMorphController.ClosedEyeMorphValues
            {
                winkL1 = 0.5f,
                winkL2 = 0.8f,
            };

            MaidFaceMorphController.ClosedEyeMorphValues result;
            Assert.True(
                MaidFaceMorphController.TryAdjustClosedEyeValues(values, out result));
            Assert.Equal(0.2f, result.winkL1, 3);
            Assert.Equal(0.8f, result.winkL2, 3);
        }

        [Fact]
        public void TryAdjustClosedEyeValues_目閉じ2種をウィンクの残り幅へ比例配分する()
        {
            // ウィンク合計 0.6 に対し、目閉じ 2 種 (比 1:3) を残り 0.4 で配分する
            var values = new MaidFaceMorphController.ClosedEyeMorphValues
            {
                close = 0.25f,
                close2 = 0.75f,
                winkR1 = 0.6f,
            };

            MaidFaceMorphController.ClosedEyeMorphValues result;
            Assert.True(
                MaidFaceMorphController.TryAdjustClosedEyeValues(values, out result));
            Assert.Equal(0.1f, result.close, 3);
            Assert.Equal(0.3f, result.close2, 3);
            Assert.Equal(0.6f, result.winkR1, 3);
        }

        [Fact]
        public void TryAdjustClosedEyeValues_目閉じが両方0でもNaNにならない()
        {
            // ウィンク単体が 1 を超える壊れたデータ。0 除算を避けて配分を見送る
            var values = new MaidFaceMorphController.ClosedEyeMorphValues
            {
                winkL1 = 1.5f,
            };

            MaidFaceMorphController.ClosedEyeMorphValues result;
            MaidFaceMorphController.TryAdjustClosedEyeValues(values, out result);
            Assert.False(float.IsNaN(result.close));
            Assert.False(float.IsNaN(result.close2));
        }
    }
}
