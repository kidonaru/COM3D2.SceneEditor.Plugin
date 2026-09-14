using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// リムライトの光源方向は汎用の LerpFrom を通る。
    /// クォータニオン 4 成分を独立に補間すると単位長も最短経路も壊れるため、
    /// まとめて slerp されることを固定する
    /// </summary>
    public class RimlightRotationLerpTests
    {
        private static TransformDataRimlight CreateRimlight(Quaternion rotation)
        {
            var trans = new TransformDataRimlight();
            trans.Initialize("Rimlight");
            trans.rotation = rotation;
            return trans;
        }

        [Fact]
        public void LerpFrom_回転は単位長を保つ()
        {
            var start = CreateRimlight(QuaternionUtils.EulerToQuaternion(Vector3.zero));
            var end = CreateRimlight(QuaternionUtils.EulerToQuaternion(new Vector3(0f, 170f, 0f)));
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            var q = current.rotation;
            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            Assert.Equal(1f, magnitude, 3);
        }

        [Fact]
        public void LerpFrom_中点は始点と終点から等距離になる()
        {
            var startRotation = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var endRotation = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 170f, 0f));

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            var q = current.rotation;
            Assert.Equal(
                Quaternion.Angle(startRotation, q),
                Quaternion.Angle(q, endRotation),
                2);
        }

        [Fact]
        public void LerpFrom_端点では始点と終点そのものになる()
        {
            var startRotation = QuaternionUtils.EulerToQuaternion(new Vector3(10f, 20f, 30f));
            var endRotation = QuaternionUtils.EulerToQuaternion(new Vector3(-40f, 100f, 5f));

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0f);
            Assert.Equal(0f, Quaternion.Angle(startRotation, current.rotation), 2);

            current.LerpFrom(start, end, 0f, 1f, 1f);
            Assert.Equal(0f, Quaternion.Angle(endRotation, current.rotation), 2);
        }

        [Fact]
        public void LerpFrom_内積が負でも最短経路を通る()
        {
            // 200 度ぶん回した向き。素直に成分補間すると 160 度側ではなく遠回りになる
            var startRotation = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var endRotation = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 200f, 0f));

            var start = CreateRimlight(startRotation);
            var end = CreateRimlight(endRotation);
            var current = CreateRimlight(Quaternion.identity);

            current.LerpFrom(start, end, 0f, 1f, 0.5f);

            // 最短経路なら中点は始点から約 80 度 (160 度の半分)
            Assert.Equal(80f, Quaternion.Angle(startRotation, current.rotation), 1);
        }

        [Fact]
        public void LerpFrom_回転4成分がそろわない壊れたデータは区間開始値へ倒す()
        {
            var start = CreateRimlight(QuaternionUtils.EulerToQuaternion(new Vector3(10f, 20f, 30f)));
            var end = CreateRimlight(QuaternionUtils.EulerToQuaternion(new Vector3(-40f, 100f, 5f)));
            var current = CreateRimlight(QuaternionUtils.EulerToQuaternion(new Vector3(80f, 80f, 80f)));

            // 回転 4 成分 (values[0..3]) の途中で値が途切れたデータを模す。
            // LerpFrom は start / end / 自身の最小値数までしか回さないので、
            // 値数 2 の型を start に渡すと回転の切れ端だけが範囲に入る
            var truncated = new TransformDataShapeKey();
            truncated.Initialize("truncated");
            truncated.values[0].value = start.values[0].value;
            truncated.values[1].value = start.values[1].value;

            current.LerpFrom(truncated, end, 0f, 1f, 0.5f);

            // 前フレームの姿勢が残ると固まってしまうので、開始値をコピーする
            Assert.Equal(start.values[0].value, current.values[0].value, 4);
            Assert.Equal(start.values[1].value, current.values[1].value, 4);
        }
    }
}
