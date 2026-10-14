using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TransformDataCameraHeightTests
    {
        private static TransformDataCameraHeight Create()
        {
            var trans = new TransformDataCameraHeight();
            trans.Initialize("height");
            return trans;
        }

        [Fact]
        public void 値は1個でタンジェントを持たない()
        {
            var trans = Create();
            Assert.Equal(TransformType.CameraHeight, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.False(trans.hasPosition);
            Assert.False(trans.hasEulerAngles);
            Assert.False(trans.hasScale);
            Assert.False(trans.hasTangent);
        }

        [Fact]
        public void 基準メイドの既定は補正なし()
        {
            Assert.Equal(-1f, Create().GetCustomValueInfo("maidSlotNo").defaultValue);
        }

        [Fact]
        public void 基準メイドはXML往復で保持される()
        {
            var trans = Create();
            trans.maidSlotNo = 2;

            var loaded = Create();
            loaded.FromXml(trans.ToXml());
            Assert.Equal(2, loaded.maidSlotNo);
        }
    }
}
