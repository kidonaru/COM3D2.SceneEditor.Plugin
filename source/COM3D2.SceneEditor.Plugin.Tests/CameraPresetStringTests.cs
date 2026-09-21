using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// カメラプリセット文字列の相互変換。
    /// 値数で形式を判別するため、旧形式 (8 値: 追従なし / 11 値: 手ブレなし) が
    /// 読めなくならないことを固定する
    /// </summary>
    public class CameraPresetStringTests
    {
        private static ScenePresetCamera CreateState()
        {
            return new ScenePresetCamera
            {
                targetPos = new Vector3(0.5f, 1.25f, -2f),
                yaw = 30f,
                pitch = -15f,
                roll = 5f,
                distance = 3.5f,
                fov = 42f,
                maidSlotNo = 1,
                maidPointType = 2,
                followRotation = true,
                shakePositionAmplitude = new Vector3(0.05f, 0.03f, 0f),
                shakeRotationAmplitude = new Vector3(1f, 0.5f, 0.25f),
                shakeFrequencyScale = 2.5f,
                shakeSeed = 1234,
            };
        }

        [Fact]
        public void 手ブレを含めて往復できる()
        {
            var restored = CameraWindow.ParseCameraPreset(
                CameraWindow.SerializeCameraPreset(CreateState()));

            Assert.NotNull(restored);
            Assert.Equal(new Vector3(0.05f, 0.03f, 0f), restored.shakePositionAmplitude);
            Assert.Equal(new Vector3(1f, 0.5f, 0.25f), restored.shakeRotationAmplitude);
            Assert.Equal(2.5f, restored.shakeFrequencyScale, 3);
            Assert.Equal(1234, restored.shakeSeed);
        }

        [Fact]
        public void 構図も往復できる()
        {
            var restored = CameraWindow.ParseCameraPreset(
                CameraWindow.SerializeCameraPreset(CreateState()));

            Assert.Equal(new Vector3(0.5f, 1.25f, -2f), restored.targetPos);
            Assert.Equal(30f, restored.yaw, 2);
            Assert.Equal(-15f, restored.pitch, 2);
            Assert.Equal(5f, restored.roll, 2);
            Assert.Equal(3.5f, restored.distance, 2);
            Assert.Equal(42f, restored.fov, 2);
            Assert.Equal(1, restored.maidSlotNo);
            Assert.Equal(2, restored.maidPointType);
            Assert.True(restored.followRotation);
        }

        [Fact]
        public void 手ブレを持たない11値の旧形式は揺れなしとして読める()
        {
            var restored = CameraWindow.ParseCameraPreset(
                "0.5000,1.2500,-2.0000,3.5000,30.00,-15.00,5.00,42.00,1,2,1");

            Assert.NotNull(restored);
            Assert.Equal(1, restored.maidSlotNo);
            // 振幅が zero なら周波数倍率・シードは参照されないので、既定値のままでよい
            Assert.Equal(Vector3.zero, restored.shakePositionAmplitude);
            Assert.Equal(Vector3.zero, restored.shakeRotationAmplitude);
        }

        [Fact]
        public void 追従を持たない8値の旧形式は未追従かつ揺れなしとして読める()
        {
            var restored = CameraWindow.ParseCameraPreset(
                "0.5000,1.2500,-2.0000,3.5000,30.00,-15.00,5.00,42.00");

            Assert.NotNull(restored);
            Assert.False(restored.hasFollow);
            Assert.Equal(Vector3.zero, restored.shakePositionAmplitude);
            Assert.Equal(Vector3.zero, restored.shakeRotationAmplitude);
        }

        [Fact]
        public void 値数が合わない文字列はnull()
        {
            Assert.Null(CameraWindow.ParseCameraPreset("1,2,3"));
        }

        [Fact]
        public void 範囲外の手ブレ値は丸められる()
        {
            var restored = CameraWindow.ParseCameraPreset(
                "0,0,0,3.5,0,0,0,42,-1,0,0," +
                "99,-1,0,99,-1,0,99,-5");

            Assert.NotNull(restored);
            Assert.Equal(
                new Vector3(
                    MTEP.TransformDataCameraShake.MaxPositionAmplitude,
                    0f, 0f),
                restored.shakePositionAmplitude);
            Assert.Equal(
                new Vector3(
                    MTEP.TransformDataCameraShake.MaxRotationAmplitude,
                    0f, 0f),
                restored.shakeRotationAmplitude);
            Assert.Equal(
                MTEP.TransformDataCameraShake.MaxFrequencyScale,
                restored.shakeFrequencyScale);
            Assert.Equal(0, restored.shakeSeed);
        }
    }
}
