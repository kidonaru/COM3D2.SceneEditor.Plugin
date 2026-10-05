using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SwingTwistAnglesTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 3);
            Assert.Equal(expected.y, actual.y, 3);
            Assert.Equal(expected.z, actual.z, 3);
        }

        /// <summary>符号違いも同じ姿勢として比べる</summary>
        private static void AssertSameRotation(Quaternion expected, Quaternion actual)
        {
            Assert.Equal(1f, Mathf.Abs(Quaternion.Dot(expected, actual)), 4);
        }

        /// <summary>棒（ローカル +Y）の向き。q * (0,1,0) を成分で展開したもの</summary>
        private static Vector3 StickDirection(Quaternion q)
        {
            return new Vector3(
                2f * (q.x * q.y - q.w * q.z),
                1f - 2f * (q.x * q.x + q.z * q.z),
                2f * (q.y * q.z + q.w * q.x));
        }

        [Theory]
        [InlineData(-10f, 0f, 0f)]
        [InlineData(0f, 0f, 30f)]
        [InlineData(0f, 45f, 0f)]
        [InlineData(-90f, 0f, 0f)]
        public void ToQuaternion_単軸ならUnityオイラー角と同じ姿勢になる(float x, float y, float z)
        {
            var angles = new Vector3(x, y, z);
            AssertSameRotation(
                QuaternionUtils.EulerToQuaternion(angles),
                SwingTwistAngles.ToQuaternion(angles));
        }

        [Theory]
        [InlineData(-10f, 0f, 0f)]
        [InlineData(-90f, 0f, 30f)]
        [InlineData(-90f, 25f, 30f)]
        [InlineData(120f, -60f, -50f)]
        [InlineData(0f, 170f, 0f)]
        [InlineData(0f, 0f, 0f)]
        public void FromQuaternion_ToQuaternionの値へ戻る(float x, float y, float z)
        {
            var angles = new Vector3(x, y, z);
            AssertVector(angles,
                SwingTwistAngles.FromQuaternion(SwingTwistAngles.ToQuaternion(angles)));
        }

        [Fact]
        public void 前へ水平に倒してもZで左右へ傾きYは棒の向きを変えない()
        {
            var forward = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 0f, 0f)));
            var leaned = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 0f, 30f)));
            var twisted = StickDirection(SwingTwistAngles.ToQuaternion(new Vector3(-90f, 30f, 0f)));

            // Z は棒を左右（X 方向）へ傾ける
            Assert.True(Mathf.Abs(leaned.x - forward.x) > 0.1f);
            // Y は棒の軸まわりのひねりなので向きは変わらない
            AssertVector(forward, twisted);
        }

        [Fact]
        public void FromQuaternion_符号反転した同じ姿勢は同じ値になる()
        {
            var q = SwingTwistAngles.ToQuaternion(new Vector3(40f, -70f, 20f));
            var negated = new Quaternion(-q.x, -q.y, -q.z, -q.w);

            AssertVector(SwingTwistAngles.FromQuaternion(q), SwingTwistAngles.FromQuaternion(negated));
        }

        [Fact]
        public void FromQuaternion_真下向きはひねり0と傾き180で返る()
        {
            var down = SwingTwistAngles.ToQuaternion(new Vector3(180f, 0f, 0f));
            var angles = SwingTwistAngles.FromQuaternion(down);

            // 180° と -180° はどちらも真下で、float の cos(π/2) の符号次第でどちらにもなる
            Assert.Equal(180f, Mathf.Abs(angles.x), 3);
            Assert.Equal(0f, angles.y, 3);
            Assert.Equal(0f, angles.z, 3);
        }

        [Fact]
        public void 真下向きでひねりがあっても変換し直した姿勢は保たれる()
        {
            var original = SwingTwistAngles.ToQuaternion(new Vector3(180f, 30f, 0f));

            AssertSameRotation(original,
                SwingTwistAngles.ToQuaternion(SwingTwistAngles.FromQuaternion(original)));
        }

        [Fact]
        public void FromQuaternion_ひねり180度は範囲の端に収まる()
        {
            var angles = SwingTwistAngles.FromQuaternion(
                SwingTwistAngles.ToQuaternion(new Vector3(0f, 180f, 0f)));

            // float 誤差で 180 と -180 のどちらにもなりうる。どちらも同じ姿勢
            Assert.Equal(180f, Mathf.Abs(angles.y), 3);
        }

        [Theory]
        [InlineData(-90f, 0f, 30f)]
        [InlineData(-90f, 40f, 0f)]
        [InlineData(30f, 200f, -75f)]
        public void 既存キーのオイラー角を変換し直しても姿勢が保たれる(float x, float y, float z)
        {
            var original = QuaternionUtils.EulerToQuaternion(new Vector3(x, y, z));
            var angles = SwingTwistAngles.FromQuaternion(original);

            AssertSameRotation(original, SwingTwistAngles.ToQuaternion(angles));
        }
    }
}
