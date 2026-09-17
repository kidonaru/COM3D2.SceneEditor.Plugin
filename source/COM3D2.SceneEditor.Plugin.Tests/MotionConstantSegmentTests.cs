using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 区間の始点・終点が同値かの判定を固定する。
    /// MotionData.isConstant が true の区間は再生時の補間計算が省略されるため、
    /// 値の差を取りこぼすと補間が止まって見える
    /// </summary>
    public class MotionConstantSegmentTests
    {
        private static TransformDataBloom CreateBloom()
        {
            var trans = new TransformDataBloom();
            trans.Initialize("Bloom");
            return trans;
        }

        private static MotionData CreateMotion(ITransformData start, ITransformData end)
        {
            return new MotionData(start, end, 0, 10);
        }

        [Fact]
        public void 全ての値が一致する区間は同値と判定される()
        {
            var start = CreateBloom();
            var end = CreateBloom();

            Assert.True(start.IsSameValues(end));
            Assert.True(CreateMotion(start, end).isConstant);
        }

        [Fact]
        public void 数値が異なる区間は同値と判定されない()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            end.intensity = start.intensity + 1f;

            Assert.False(start.IsSameValues(end));
            Assert.False(CreateMotion(start, end).isConstant);
        }

        [Fact]
        public void 色だけが異なる区間も同値と判定されない()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.color = new Color(0f, 0f, 0f, 1f);
            end.color = new Color(0f, 0f, 1f, 1f);

            Assert.False(start.IsSameValues(end));
        }

        [Fact]
        public void タンジェントの違いは同値判定に影響しない()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.intensityValue.outTangent.normalizedValue = 3f;
            end.intensityValue.inTangent.normalizedValue = 3f;

            Assert.True(start.IsSameValues(end));
        }

        [Fact]
        public void 文字列値が異なる区間は同値と判定されない()
        {
            // TransformDataSe は strValueCount = 1 (SE ファイル名)
            var start = new TransformDataSe();
            start.Initialize("se");
            var end = (TransformDataSe)start.Clone();

            Assert.True(start.IsSameValues(end));

            end.strValues[0] = "changed";

            Assert.False(start.IsSameValues(end));
        }

        [Fact]
        public void 型が異なる場合は同値と判定されない()
        {
            var bloom = CreateBloom();
            var distanceFog = new TransformDataDistanceFog();
            distanceFog.Initialize("DistanceFog");

            Assert.False(bloom.IsSameValues(distanceFog));
        }
    }
}
