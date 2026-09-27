using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>XML 由来の影の種類の丸めを固定する</summary>
    public class LightShadowValuesTests
    {
        [Theory]
        [InlineData(0, LightShadows.None)]
        [InlineData(1, LightShadows.Hard)]
        [InlineData(2, LightShadows.Soft)]
        public void 定義済みの値はそのまま読む(int value, LightShadows expected)
        {
            Assert.Equal(expected, LightShadowValues.FromInt(value));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(9)]
        public void 範囲外の値は影なしにする(int value)
        {
            Assert.Equal(LightShadows.None, LightShadowValues.FromInt(value));
        }
    }
}
