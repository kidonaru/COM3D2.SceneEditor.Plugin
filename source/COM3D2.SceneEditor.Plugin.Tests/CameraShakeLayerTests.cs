using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeLayerTests
    {
        [Fact]
        public void カメラレイヤーはカメラと手ブレの2ボーンを持つ()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(2, layer.allBoneNames.Count);
            Assert.Contains(CameraTimelineLayer.CameraBoneName, layer.allBoneNames);
            Assert.Contains(CameraTimelineLayer.ShakeBoneName, layer.allBoneNames);
        }

        [Fact]
        public void ボーン名に応じたTransformTypeを返す()
        {
            var layer = CameraTimelineLayer.Create(0);
            Assert.Equal(TransformType.Camera,
                layer.GetTransformType(CameraTimelineLayer.CameraBoneName));
            Assert.Equal(TransformType.CameraShake,
                layer.GetTransformType(CameraTimelineLayer.ShakeBoneName));
        }

        [Fact]
        public void シードは補間せず始点の値を使う()
        {
            var start = new TransformDataCameraShake();
            start.Initialize(CameraTimelineLayer.ShakeBoneName);
            var startParams = CameraShakeParams.Default;
            startParams.seed = 100;
            start.shakeParams = startParams;

            var end = new TransformDataCameraShake();
            end.Initialize(CameraTimelineLayer.ShakeBoneName);
            var endParams = CameraShakeParams.Default;
            endParams.seed = 900;
            end.shakeParams = endParams;

            // 終点に別のシードがあっても始点の値だけが使われる
            Assert.Equal(900, end.shakeParams.seed);
            Assert.Equal(100, CameraTimelineLayer.ResolveShakeSeed(start));
        }
    }
}
