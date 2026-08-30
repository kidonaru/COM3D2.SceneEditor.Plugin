using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class MainCameraRowDrawerTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(90f, 90f)]
        [InlineData(180f, 180f)]
        // 旋回で 180 を超えた角度は負側へ折り返す
        [InlineData(181f, -179f)]
        [InlineData(270f, -90f)]
        [InlineData(360f, 0f)]
        // 何周ぶんでも (-180, 180] へ収める
        [InlineData(730f, 10f)]
        [InlineData(-90f, -90f)]
        [InlineData(-190f, 170f)]
        [InlineData(-730f, -10f)]
        public void NormalizeAngle_角度を180度以内へ正規化する(float angle, float expected)
        {
            Assert.Equal(expected, MainCameraRowDrawer.NormalizeAngle(angle), 3);
        }
    }
}
