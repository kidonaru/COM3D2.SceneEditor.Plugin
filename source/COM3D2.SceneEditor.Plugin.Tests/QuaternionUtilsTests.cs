using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 純 C# 実装が Unity 実装と一致することを固定する。
    /// 期待値は稼働中の COM3D2.5 で UnityEngine.Quaternion.Euler / Slerp を評価して採った実測値
    /// （Unity 実装はネイティブ ECall でテストプロセスから呼べないため、値を写して比較する）
    /// </summary>
    public class QuaternionUtilsTests
    {
        private static void AssertQuaternion(
            float x, float y, float z, float w, Quaternion actual)
        {
            Assert.Equal(x, actual.x, 5);
            Assert.Equal(y, actual.y, 5);
            Assert.Equal(z, actual.z, 5);
            Assert.Equal(w, actual.w, 5);
        }

        [Fact]
        public void EulerToQuaternion_Unityと同じ値を返す()
        {
            AssertQuaternion(
                0.360042155f, 0.196628183f, 0.3033718f, 0.860042155f,
                QuaternionUtils.EulerToQuaternion(new Vector3(30f, 40f, 50f)));
        }

        [Fact]
        public void EulerToQuaternion_負の角や1回転を超える角でもUnityと一致する()
        {
            AssertQuaternion(
                0.327371269f, 0.397372425f, 0.754721642f, -0.40659368f,
                QuaternionUtils.EulerToQuaternion(new Vector3(-120f, 200f, 45f)));
        }

        [Fact]
        public void EulerToQuaternion_ゼロはidentityになる()
        {
            AssertQuaternion(0f, 0f, 0f, 1f, QuaternionUtils.EulerToQuaternion(Vector3.zero));
        }

        [Fact]
        public void Slerp_Unityと同じ値を返す()
        {
            var start = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 170f, 0f));

            AssertQuaternion(0f, 0.4702419f, 0f, 0.882537544f,
                QuaternionUtils.Slerp(start, end, 0.33f));
        }

        [Fact]
        public void Slerp_任意の姿勢どうしでもUnityと一致する()
        {
            var start = QuaternionUtils.EulerToQuaternion(new Vector3(10f, 20f, 30f));
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(-40f, 100f, 5f));

            AssertQuaternion(-0.09685339f, 0.5854833f, 0.291413128f, 0.75027144f,
                QuaternionUtils.Slerp(start, end, 0.7f));
        }

        [Fact]
        public void Slerp_端点では始点と終点そのものになる()
        {
            var start = QuaternionUtils.EulerToQuaternion(new Vector3(10f, 20f, 30f));
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(-40f, 100f, 5f));

            Assert.Equal(0f, Quaternion.Angle(start, QuaternionUtils.Slerp(start, end, 0f)), 3);
            Assert.Equal(0f, Quaternion.Angle(end, QuaternionUtils.Slerp(start, end, 1f)), 3);
        }

        [Fact]
        public void Slerp_中点は始点と終点から等距離になる()
        {
            var start = QuaternionUtils.EulerToQuaternion(new Vector3(10f, 20f, 30f));
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(-40f, 100f, 5f));

            var mid = QuaternionUtils.Slerp(start, end, 0.5f);

            Assert.Equal(
                Quaternion.Angle(start, mid),
                Quaternion.Angle(mid, end),
                3);
        }

        [Fact]
        public void Slerp_内積が負でも最短経路を通る()
        {
            // 200 度ぶん回した向き。最短経路なら 160 度側を通る
            var start = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 200f, 0f));

            var mid = QuaternionUtils.Slerp(start, end, 0.5f);

            Assert.Equal(80f, Quaternion.Angle(start, mid), 2);
        }

        [Fact]
        public void Slerp_tは0から1へ丸められる()
        {
            var start = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 90f, 0f));

            Assert.Equal(0f, Quaternion.Angle(start, QuaternionUtils.Slerp(start, end, -1f)), 3);
            Assert.Equal(0f, Quaternion.Angle(end, QuaternionUtils.Slerp(start, end, 2f)), 3);
        }

        [Fact]
        public void Slerp_補間結果は常に単位長になる()
        {
            var start = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 0f, 0f));
            var end = QuaternionUtils.EulerToQuaternion(new Vector3(0f, 170f, 0f));

            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                var q = QuaternionUtils.Slerp(start, end, t);
                var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                Assert.Equal(1f, magnitude, 4);
            }
        }

        [Fact]
        public void Normalize_全成分ゼロはidentityへ倒す()
        {
            AssertQuaternion(0f, 0f, 0f, 1f, QuaternionUtils.Normalize(new Quaternion(0f, 0f, 0f, 0f)));
        }
    }
}
