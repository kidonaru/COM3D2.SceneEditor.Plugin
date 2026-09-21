using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TransformDataCameraShakeTests
    {
        private static TransformDataCameraShake Create()
        {
            var trans = new TransformDataCameraShake();
            trans.Initialize("shake");
            return trans;
        }

        [Fact]
        public void 値は8個で位置回転スケールを持たない()
        {
            var trans = Create();
            Assert.Equal(8, trans.valueCount);
            Assert.False(trans.hasPosition);
            Assert.False(trans.hasEulerAngles);
            Assert.False(trans.hasScale);
            Assert.True(trans.hasTangent);
        }

        [Fact]
        public void Initialize直後は全値0で周波数倍率の既定はCustomValueInfoが持つ()
        {
            // Initialize は値配列を確保するだけで CustomValueInfo の defaultValue は適用しない
            // (適用するのは Reset)。キー化時は shakeParams で 8 値すべてを上書きする
            var trans = Create();
            var p = trans.shakeParams;
            Assert.Equal(Vector3.zero, p.positionAmplitude);
            Assert.Equal(Vector3.zero, p.rotationAmplitude);
            Assert.Equal(0f, p.frequencyScale);
            Assert.Equal(0, p.seed);

            Assert.Equal(1f, trans.GetCustomValueInfo("frequencyScale").defaultValue);
        }

        [Fact]
        public void パラメータを書いて読み戻せる()
        {
            var trans = Create();
            trans.shakeParams = new CameraShakeParams
            {
                positionAmplitude = new Vector3(0.01f, 0.02f, 0.03f),
                rotationAmplitude = new Vector3(1f, 2f, 3f),
                frequencyScale = 2.5f,
                seed = 123,
            };

            var p = trans.shakeParams;
            Assert.Equal(new Vector3(0.01f, 0.02f, 0.03f), p.positionAmplitude);
            Assert.Equal(new Vector3(1f, 2f, 3f), p.rotationAmplitude);
            Assert.Equal(2.5f, p.frequencyScale);
            Assert.Equal(123, p.seed);
        }

        [Fact]
        public void カスタム値のindexが値配列と対応する()
        {
            var trans = Create();
            Assert.Equal(0, trans.GetCustomValueInfo("posAmplitudeX").index);
            Assert.Equal(2, trans.GetCustomValueInfo("posAmplitudeZ").index);
            Assert.Equal(3, trans.GetCustomValueInfo("rotAmplitudeX").index);
            Assert.Equal(5, trans.GetCustomValueInfo("rotAmplitudeZ").index);
            Assert.Equal(6, trans.GetCustomValueInfo("frequencyScale").index);
            Assert.Equal(7, trans.GetCustomValueInfo("seed").index);
        }

        [Fact]
        public void 型はCameraShake()
        {
            Assert.Equal(TransformType.CameraShake, Create().type);
        }
    }
}
