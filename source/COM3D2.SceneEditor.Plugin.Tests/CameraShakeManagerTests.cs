using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeManagerTests
    {
        [Fact]
        public void 完全一致なら同一とみなす()
        {
            var pos = new Vector3(1f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.True(CameraShakeManager.IsSameTransform(pos, pos, rot, rot));
        }

        [Fact]
        public void 誤差の範囲内なら同一とみなす()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(1f + 1e-6f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.True(CameraShakeManager.IsSameTransform(a, b, rot, rot));
        }

        [Fact]
        public void 位置が動いていれば別物とみなす()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(1.05f, 2f, 3f);
            var rot = TestQuaternions.AroundY(30f);
            Assert.False(CameraShakeManager.IsSameTransform(a, b, rot, rot));
        }

        [Fact]
        public void 回転が動いていれば別物とみなす()
        {
            var pos = new Vector3(1f, 2f, 3f);
            var ra = TestQuaternions.AroundY(30f);
            var rb = TestQuaternions.AroundY(35f);
            Assert.False(CameraShakeManager.IsSameTransform(pos, pos, ra, rb));
        }

        [Fact]
        public void 符号が逆の同一回転は同一とみなす()
        {
            // Quaternion は q と -q が同じ姿勢を表す
            var pos = Vector3.zero;
            var ra = TestQuaternions.AroundY(30f);
            var rb = TestQuaternions.Negate(ra);
            Assert.True(CameraShakeManager.IsSameTransform(pos, pos, ra, rb));
        }

        [Fact]
        public void ライブ値の既定は振幅0で周波数倍率1()
        {
            CameraShakeManager.instance.shakeParams =
                COM3D2.MotionTimelineEditor.Plugin.CameraShakeParams.Default;
            var p = CameraShakeManager.instance.shakeParams;
            Assert.Equal(1f, p.frequencyScale);
            Assert.True(p.isZero);
        }
    }
}
