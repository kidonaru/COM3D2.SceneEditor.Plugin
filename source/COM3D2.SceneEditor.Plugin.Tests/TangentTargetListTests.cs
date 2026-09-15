using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タンジェント編集対象 (TangentTargetList) が、クォータニオン格納の回転を
    /// 4 成分ひとかたまりとして扱うことの確認
    /// </summary>
    public class TangentTargetListTests
    {
        private static TangentTarget AxisTarget(TangentValueType valueType)
        {
            return new TangentTarget
            {
                name = valueType.ToString(),
                valueType = valueType,
                id = TangentTargetList.AxisIdPrefix + valueType,
            };
        }

        private static TransformDataMove CreateMove()
        {
            var trans = new TransformDataMove();
            trans.Initialize("Move");
            return trans;
        }

        [Theory]
        [InlineData(TangentValueType.X回転)]
        [InlineData(TangentValueType.Y回転)]
        [InlineData(TangentValueType.Z回転)]
        [InlineData(TangentValueType.W回転)]
        [InlineData(TangentValueType.回転)]
        public void クォータニオン回転は軸別指定でも4成分すべてのタンジェントを返す(
            TangentValueType valueType)
        {
            var trans = CreateMove();
            var target = AxisTarget(valueType);

            var outTangents = TangentTargetList.GetTangents(trans, target, isOut: true);
            var inTangents = TangentTargetList.GetTangents(trans, target, isOut: false);

            Assert.Equal(4, outTangents.Length);
            Assert.Equal(4, inTangents.Length);
        }

        [Theory]
        [InlineData(TangentValueType.X回転)]
        [InlineData(TangentValueType.Y回転)]
        [InlineData(TangentValueType.Z回転)]
        [InlineData(TangentValueType.W回転)]
        public void クォータニオン回転は軸別指定でも4成分すべての値を返す(TangentValueType valueType)
        {
            var trans = CreateMove();
            var target = AxisTarget(valueType);

            var values = TangentTargetList.GetValues(trans, target);

            // タンジェント列と添字が対応している必要がある (KeyFrameTangentDrawer の前提)
            Assert.Equal(4, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                Assert.Same(trans.rotationValues[i], values[i]);
            }
        }

        [Fact]
        public void 移動の軸別指定はその軸1成分だけを返す()
        {
            var trans = CreateMove();
            var target = AxisTarget(TangentValueType.Y移動);

            var values = TangentTargetList.GetValues(trans, target);

            Assert.Single(values);
            Assert.Same(trans.positionValues[1], values[0]);
        }

        [Fact]
        public void Euler格納の回転は軸別指定でその軸1成分だけを返す()
        {
            var trans = new TransformDataBG();
            trans.Initialize("BG");
            Assert.False(trans.hasRotation);
            Assert.True(trans.hasEulerAngles);

            var target = AxisTarget(TangentValueType.Y回転);
            var values = TangentTargetList.GetValues(trans, target);

            Assert.Single(values);
            Assert.Same(trans.eulerAnglesValues[1], values[0]);
        }
    }
}
