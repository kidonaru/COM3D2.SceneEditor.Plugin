using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 差分キーフレーム登録の「変化なし」判定を固定する。
    ///
    /// 固定 IK は編集モード中に毎フレームチェーンを解き直すため、編集開始スナップショットと
    /// 同じ姿勢でも四元数の符号反転 (q と -q) や 1e-7 級の浮動小数ノイズが乗る。
    /// これを完全一致で比べると触っていないボーンまで登録されてしまう
    /// </summary>
    public class TransformDataApproximateEqualityTests
    {
        private static TransformDataRotation CreateRotation(
            Quaternion rotation, string name = "Bip01 R Forearm")
        {
            var trans = new TransformDataRotation();
            trans.Initialize(name);
            trans.rotation = rotation;
            return trans;
        }

        [Fact]
        public void 符号反転した四元数は同じ回転として扱う()
        {
            var q = new Quaternion(0f, 0f, 0.8504f, 0.5262f);
            var a = CreateRotation(q);
            var b = CreateRotation(new Quaternion(-q.x, -q.y, -q.z, -q.w));

            Assert.True(TransformDataDiff.IsApproximatelyEqual(a, b));
        }

        [Fact]
        public void 浮動小数ノイズ程度の差は変化なしとして扱う()
        {
            var a = CreateRotation(new Quaternion(-0.4733f, 0.8701f, 0.0221f, -0.1359f));
            var b = CreateRotation(new Quaternion(-0.4733f + 6e-8f, 0.8701f - 2e-7f, 0.0221f, -0.1359f));

            Assert.True(TransformDataDiff.IsApproximatelyEqual(a, b));
        }

        [Fact]
        public void 目に見える回転差は変化ありとして扱う()
        {
            var a = CreateRotation(Quaternion.identity);
            var b = CreateRotation(new Quaternion(0.0087f, 0f, 0f, 0.99996f)); // 約 1 度

            Assert.False(TransformDataDiff.IsApproximatelyEqual(a, b));
        }

        /// <summary>
        /// 回転成分の除外は参照同一で判定する。ValueData.Equals は値比較なので、
        /// 値で除外すると回転成分と同じ値 (identity の 0) を持つ位置成分の変化が見落とされる
        /// </summary>
        [Fact]
        public void 回転成分と同じ値を持つ位置成分の変化を見落とさない()
        {
            var a = new TransformDataStageLaserController();
            a.Initialize("StageLaserController (1)");
            a.rotation = Quaternion.identity;
            a.position = Vector3.zero;

            var b = new TransformDataStageLaserController();
            b.Initialize("StageLaserController (1)");
            b.rotation = Quaternion.identity;
            b.position = new Vector3(0f, 0.5f, 0f);

            Assert.False(TransformDataDiff.IsApproximatelyEqual(a, b));
        }

        [Fact]
        public void 名前が違えば変化ありとして扱う()
        {
            var a = CreateRotation(Quaternion.identity);
            var b = CreateRotation(Quaternion.identity, "Bip01 L Forearm");

            Assert.False(TransformDataDiff.IsApproximatelyEqual(a, b));
        }
    }
}
