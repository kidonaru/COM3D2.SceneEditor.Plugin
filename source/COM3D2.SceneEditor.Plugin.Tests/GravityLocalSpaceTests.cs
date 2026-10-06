using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>重力のローカル座標（Bip01 基準）の回転と ±1 への収め方を固定する</summary>
    public class GravityLocalSpaceTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.InRange(actual.x, expected.x - Tolerance, expected.x + Tolerance);
            Assert.InRange(actual.y, expected.y - Tolerance, expected.y + Tolerance);
            Assert.InRange(actual.z, expected.z - Tolerance, expected.z + Tolerance);
        }

        [Fact]
        public void 基準姿勢の回転は実測値()
        {
            // maid_stand01 の Bip01 を実測した値。変えると既存シーンの重力の向きが変わる
            var q = GravityLocalSpace.Bip01BaseRotation;
            Assert.Equal(-0.5415668f, q.x);
            Assert.Equal(0.5415668f, q.y);
            Assert.Equal(0.4546487f, q.z);
            Assert.Equal(0.4546487f, q.w);
        }

        [Fact]
        public void 基準姿勢ではoffsetがそのまま返る()
        {
            var force = GravityLocalSpace.ToForce(
                GravityLocalSpace.Bip01BaseRotation, new Vector3(0.2f, -1f, 0.3f));

            AssertVector(new Vector3(0.2f, -1f, 0.3f), force);
        }

        [Fact]
        public void Bip01をX軸まわりに90度回すと下向きの重力も同じだけ回る()
        {
            // 寝そべり相当: 体ごとワールド X 軸まわりに 90 度倒す。(0,-1,0) は (0,0,-1) へ回る
            var bip01 = TestQuaternions.AroundX(90f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(0f, -1f, 0f));

            AssertVector(new Vector3(0f, 0f, -1f), force);
        }

        [Fact]
        public void メイドのY軸回転にも追従する()
        {
            var bip01 = TestQuaternions.AroundY(90f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(0f, 0f, 1f));

            AssertVector(new Vector3(1f, 0f, 0f), force);
        }

        [Fact]
        public void FitToUnitBox_範囲内ならそのまま()
        {
            AssertVector(new Vector3(0.5f, -1f, 0f),
                GravityLocalSpace.FitToUnitBox(new Vector3(0.5f, -1f, 0f)));
        }

        [Fact]
        public void FitToUnitBox_はみ出したら最大成分が1になるよう方向を保って縮める()
        {
            var fitted = GravityLocalSpace.FitToUnitBox(new Vector3(2f, -1f, 0.5f));

            AssertVector(new Vector3(1f, -0.5f, 0.25f), fitted);
        }

        [Fact]
        public void 回転後も範囲内なら縮めない()
        {
            // (1,1,0) を Y 軸まわりに 45 度回すと (0.7071, 1, -0.7071)
            var bip01 = TestQuaternions.AroundY(45f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(1f, 1f, 0f));

            AssertVector(new Vector3(0.7071f, 1f, -0.7071f), force);
        }

        [Fact]
        public void 回転で成分が1を超えると方向を保って縮む()
        {
            // (1,0,1) を Y 軸まわりに 45 度回すと (1.4142, 0, 0) になる。最大成分 1 へ縮めて (1,0,0)
            var bip01 = TestQuaternions.AroundY(45f) * GravityLocalSpace.Bip01BaseRotation;

            var force = GravityLocalSpace.ToForce(bip01, new Vector3(1f, 0f, 1f));

            AssertVector(new Vector3(1f, 0f, 0f), force);
        }
    }
}
