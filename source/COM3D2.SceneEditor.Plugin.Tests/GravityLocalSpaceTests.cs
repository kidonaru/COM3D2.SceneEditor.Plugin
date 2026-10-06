using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>重力のローカル座標（基準ボーン基準）の回転と ±1 への収め方を固定する</summary>
    public class GravityLocalSpaceTests
    {
        private const float Tolerance = 1e-4f;

        // 回転の計算は基準に依らないので、代表として頭の値を使う
        private static readonly Quaternion BaseRotation = GravityLocalSpace.HeadBaseRotation;

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.InRange(actual.x, expected.x - Tolerance, expected.x + Tolerance);
            Assert.InRange(actual.y, expected.y - Tolerance, expected.y + Tolerance);
            Assert.InRange(actual.z, expected.z - Tolerance, expected.z + Tolerance);
        }

        [Fact]
        public void 頭の基準姿勢の回転は実測値()
        {
            // maid_stand01 の Bip01 Head を実測した値。変えるとローカルとワールドが一致する姿勢がずれる
            var q = GravityLocalSpace.HeadBaseRotation;
            Assert.Equal(0.5777411f, q.x);
            Assert.Equal(-0.4202374f, q.y);
            Assert.Equal(-0.5255319f, q.z);
            Assert.Equal(0.4619873f, q.w);
        }

        [Fact]
        public void 骨盤の基準姿勢の回転は実測値()
        {
            // maid_stand01 の Bip01 Pelvis を実測した値。変えるとローカルとワールドが一致する姿勢がずれる
            var q = GravityLocalSpace.PelvisBaseRotation;
            Assert.Equal(0.5018583f, q.x);
            Assert.Equal(-0.4970104f, q.y);
            Assert.Equal(-0.5237849f, q.z);
            Assert.Equal(0.4762020f, q.w);
        }

        [Fact]
        public void 基準姿勢ではoffsetがそのまま返る()
        {
            var force = GravityLocalSpace.ToForce(BaseRotation, BaseRotation, new Vector3(0.2f, -1f, 0.3f));

            AssertVector(new Vector3(0.2f, -1f, 0.3f), force);
        }

        [Fact]
        public void 骨盤も基準姿勢ではoffsetがそのまま返る()
        {
            var pelvis = GravityLocalSpace.PelvisBaseRotation;

            var force = GravityLocalSpace.ToForce(pelvis, pelvis, new Vector3(0.2f, -1f, 0.3f));

            AssertVector(new Vector3(0.2f, -1f, 0.3f), force);
        }

        [Fact]
        public void ボーンをX軸まわりに90度回すと下向きの重力も同じだけ回る()
        {
            // 寝そべり相当: ボーンをワールド X 軸まわりに 90 度倒す。(0,-1,0) は (0,0,-1) へ回る
            var bone = TestQuaternions.AroundX(90f) * BaseRotation;

            var force = GravityLocalSpace.ToForce(bone, BaseRotation, new Vector3(0f, -1f, 0f));

            AssertVector(new Vector3(0f, 0f, -1f), force);
        }

        [Fact]
        public void 骨盤基準でもX軸まわりに90度回すと下向きの重力も同じだけ回る()
        {
            var pelvis = GravityLocalSpace.PelvisBaseRotation;
            var bone = TestQuaternions.AroundX(90f) * pelvis;

            var force = GravityLocalSpace.ToForce(bone, pelvis, new Vector3(0f, -1f, 0f));

            AssertVector(new Vector3(0f, 0f, -1f), force);
        }

        [Fact]
        public void メイドのY軸回転にも追従する()
        {
            var bone = TestQuaternions.AroundY(90f) * BaseRotation;

            var force = GravityLocalSpace.ToForce(bone, BaseRotation, new Vector3(0f, 0f, 1f));

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
            var bone = TestQuaternions.AroundY(45f) * BaseRotation;

            var force = GravityLocalSpace.ToForce(bone, BaseRotation, new Vector3(1f, 1f, 0f));

            AssertVector(new Vector3(0.7071f, 1f, -0.7071f), force);
        }

        [Fact]
        public void 回転で成分が1を超えると方向を保って縮む()
        {
            // (1,0,1) を Y 軸まわりに 45 度回すと (1.4142, 0, 0) になる。最大成分 1 へ縮めて (1,0,0)
            var bone = TestQuaternions.AroundY(45f) * BaseRotation;

            var force = GravityLocalSpace.ToForce(bone, BaseRotation, new Vector3(1f, 0f, 1f));

            AssertVector(new Vector3(1f, 0f, 0f), force);
        }
    }
}
