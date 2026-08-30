using COM3D2.SceneEditor.Plugin;
using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class KeyFrameTangentLogicTests
    {
        private static MTEP.TangentPair Pair(float outT, float inT)
        {
            return new MTEP.TangentPair { outTangent = outT, inTangent = inT };
        }

        [Fact]
        public void 全要素が一致すればその値を返す()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.5f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.Equal(0.5f, outT);
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 混在した側だけNaNになる()
        {
            var tangents = new List<MTEP.TangentPair> { Pair(0.5f, 1f), Pair(0.7f, 1f) };
            KeyFrameTangentLogic.GetUniformTangents(tangents, out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.Equal(1f, inT);
        }

        [Fact]
        public void 空ならどちらもNaN()
        {
            KeyFrameTangentLogic.GetUniformTangents(new List<MTEP.TangentPair>(), out var outT, out var inT);
            Assert.True(float.IsNaN(outT));
            Assert.True(float.IsNaN(inT));
        }
    }
}
