using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// クォータニオン保持へ移した型の初期姿勢を固定する。
    ///
    /// <see cref="TransformDataBase.Reset"/> は hasRotation の型で initialRotation を読むが、
    /// UI 側 (キーのリセット・前キーとの比較) は initialEulerAngles を読み続ける。
    /// 片方だけ override すると、もう片方が既定値 (identity / ゼロ) へ静かに化ける
    /// </summary>
    public class RotationInitialValueTests
    {
        public static TheoryData<ITransformData, Vector3> InitialRotationCases()
        {
            return new TheoryData<ITransformData, Vector3>
            {
                { new TransformDataStageLight(), new Vector3(90f, 0f, 0f) },
                { new TransformDataStageLaser(), StageLaser.DefaultEulerAngles },
                { new TransformDataStageLaserController(), StageLaserController.DefaultEulerAngles },
                {
                    new TransformDataPsylliumTransform(),
                    TransformDataPsylliumTransform.defaultConfig.eulerAnglesLeft
                },
            };
        }

        [Theory]
        [MemberData(nameof(InitialRotationCases))]
        public void 初期姿勢はオイラー角側とクォータニオン側で一致する(
            ITransformData trans, Vector3 expectedEulerAngles)
        {
            // UI が読む側
            Assert.Equal(expectedEulerAngles.x, trans.initialEulerAngles.x, 3);
            Assert.Equal(expectedEulerAngles.y, trans.initialEulerAngles.y, 3);
            Assert.Equal(expectedEulerAngles.z, trans.initialEulerAngles.z, 3);

            // Reset が読む側
            var expected = QuaternionUtils.EulerToQuaternion(expectedEulerAngles);
            Assert.Equal(0f, Quaternion.Angle(expected, trans.initialRotation), 2);
        }

        [Fact]
        public void サイリウムの右手も初期姿勢が両側で一致する()
        {
            var trans = new TransformDataPsylliumTransform();
            var expectedEulerAngles = TransformDataPsylliumTransform.defaultConfig.eulerAnglesRight;

            Assert.Equal(expectedEulerAngles.x, trans.initialSubEulerAngles.x, 3);
            Assert.Equal(expectedEulerAngles.z, trans.initialSubEulerAngles.z, 3);

            var expected = QuaternionUtils.EulerToQuaternion(expectedEulerAngles);
            Assert.Equal(0f, Quaternion.Angle(expected, trans.initialSubRotation), 2);
        }
    }
}
