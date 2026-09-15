using System;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TangentValueCoverageTests
    {
        [Fact]
        public void 背景色はタンジェントを持ち色成分は対象外()
        {
            var trans = new TransformDataBGColor();
            trans.Initialize("BGColor");

            Assert.True(trans.hasTangent);
            // valueCount は色 RGB の 3 つだけなので、色を除くと対象は空になる
            Assert.Empty(trans.tangentValues);
        }

        [Fact]
        public void 地面色は位置と広さと表示にタンジェントを持つ()
        {
            var trans = new TransformDataBGGroundColor();
            trans.Initialize("BGGroundColor");

            Assert.True(trans.hasTangent);
            // valueCount 10 のうち色 RGB 3 つを除いた 7 つ (位置 3 + 広さ 3 + 表示 1)
            Assert.Equal(7, trans.tangentValues.Length);
        }

        [Fact]
        public void PNG配置のタンジェント対象にSZが含まれる()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("Png0");

            // ValueData は値ベースの Equals を持つため、既定値 0 の別インスタンスに
            // マッチしないよう参照同一性で確かめる
            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.scalezValue));
        }
    }
}
