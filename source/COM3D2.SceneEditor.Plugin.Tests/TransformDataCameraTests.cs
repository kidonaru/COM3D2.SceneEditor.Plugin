using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TransformDataCameraTests
    {
        private static TransformDataCamera Create()
        {
            var trans = new TransformDataCamera();
            trans.Initialize(CameraTimelineLayer.CameraBoneName);
            return trans;
        }

        [Fact]
        public void 距離とFoVは拡縮ではなく独立したカスタム値()
        {
            var trans = Create();
            Assert.Equal(13, trans.valueCount);
            Assert.False(trans.hasScale);

            var map = trans.GetCustomValueInfoMap();
            Assert.True(map.ContainsKey("distance"));
            Assert.True(map.ContainsKey("fov"));
            Assert.Equal((int)TransformDataCamera.Index.Distance, map["distance"].index);
            Assert.Equal((int)TransformDataCamera.Index.Fov, map["fov"].index);
            Assert.Equal(1f, map["distance"].defaultValue);
            Assert.Equal(35f, map["fov"].defaultValue);
        }

        [Fact]
        public void 距離とFoVの添字は旧拡縮のXYと同じ()
        {
            // 旧データを無変換で読むため、添字は据え置きであることを保証する
            Assert.Equal(7, (int)TransformDataCamera.Index.Distance);
            Assert.Equal(8, (int)TransformDataCamera.Index.Fov);

            var trans = Create();
            trans.distance = 2.5f;
            trans.fov = 60f;

            Assert.Equal(2.5f, trans.values[7].value);
            Assert.Equal(60f, trans.values[8].value);
        }

        [Fact]
        public void リセットすると旧initialScaleと同じ距離1とFoV35に戻る()
        {
            var trans = Create();
            trans.distance = 12f;
            trans.fov = 90f;

            trans.Reset();

            Assert.Equal(1f, trans.distance);
            Assert.Equal(35f, trans.fov);
        }

        [Fact]
        public void 旧XMLの拡縮XYが距離とFoVとして読める()
        {
            var trans = Create();
            var values = new float[13];
            values[7] = 3.5f;
            values[8] = 45f;

            trans.FromXml(new TransformXml
            {
                name = CameraTimelineLayer.CameraBoneName,
                type = TransformType.Camera,
                values = values,
                inTangents = new float[13],
                outTangents = new float[13],
                inSmoothBit = 0,
                outSmoothBit = 0,
            });

            Assert.Equal(3.5f, trans.distance);
            Assert.Equal(45f, trans.fov);
        }

        [Fact]
        public void 追従設定を持たない旧10値XMLでも距離とFoVは維持される()
        {
            var trans = Create();
            var values = new float[10];
            values[7] = 1.2f;
            values[8] = 20f;

            trans.FromXml(new TransformXml
            {
                name = CameraTimelineLayer.CameraBoneName,
                type = TransformType.Camera,
                values = values,
                inTangents = new float[10],
                outTangents = new float[10],
                inSmoothBit = 0,
                outSmoothBit = 0,
            });

            Assert.Equal(1.2f, trans.distance);
            Assert.Equal(20f, trans.fov);
            Assert.Equal(-1, trans.maidSlotNo);
            Assert.Equal(MaidPointType.Crotch, trans.maidPointType);
            Assert.False(trans.followRotation);
        }
    }
}
