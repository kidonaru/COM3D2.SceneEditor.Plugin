using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;
// 囲みの名前空間 COM3D2.SceneEditor.Plugin にも PluginUtils があるため明示的に別名を張る
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 集約型レイヤー (ポストエフェクト・マテリアル) の再生に使う値ごとの補間を固定する。
    /// 色は線形、数値はその値自身のタンジェント、Bool / Int と文字列は区間開始値
    /// </summary>
    public class TransformLerpFromTests
    {
        private static TransformDataBloom CreateBloom()
        {
            var trans = new TransformDataBloom();
            trans.Initialize("Bloom");
            return trans;
        }

        private static void SetTangent(TangentData tangent, float value)
        {
            tangent.isSmooth = false;
            tangent.normalizedValue = value;
            tangent.UpdateValue(1f);
        }

        private static TransformDataBloom Lerp(
            TransformDataBloom start, TransformDataBloom end, float t)
        {
            var scratch = (TransformDataBloom)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void 色は線形補間される()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.color = new Color(0f, 0f, 0f, 0f);
            end.color = new Color(1f, 1f, 1f, 1f);

            var mid = Lerp(start, end, 0.25f);

            Assert.Equal(0.25f, mid.color.r, 4);
            Assert.Equal(0.25f, mid.color.a, 4);
        }

        [Fact]
        public void 数値はその値自身のタンジェントで補間される()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.intensity = 0f;
            end.intensity = 1f;
            start.threshold = 0f;
            end.threshold = 1f;

            // intensity だけ両端のタンジェントを 0 にし、threshold には傾きを与える。
            // TangentData.value は setter が無いので normalizedValue + UpdateValue で入れる
            SetTangent(start.intensityValue.outTangent, 0f);
            SetTangent(end.intensityValue.inTangent, 0f);
            SetTangent(start.thresholdValue.outTangent, 3f);
            SetTangent(end.thresholdValue.inTangent, 3f);

            // t=0.5 は両端のタンジェントが対称だと曲線の形に関わらず中点になるので、
            // 差が出る 0.25 で見る
            var mid = Lerp(start, end, 0.25f);

            var expectedIntensity = MTEP.PluginUtils.HermiteValue(
                0f, 1f, start.intensityValue, end.intensityValue, 0.25f);
            var expectedThreshold = MTEP.PluginUtils.HermiteValue(
                0f, 1f, start.thresholdValue, end.thresholdValue, 0.25f);

            Assert.Equal(expectedIntensity, mid.intensity, 4);
            Assert.Equal(expectedThreshold, mid.threshold, 4);
            // タンジェントが違えば中間値も違う (一括補間なら一致してしまう)
            Assert.NotEqual(mid.intensity, mid.threshold, 4);
            // 値が Hold に誤分類されると両方 0 のままになる (lerpKinds の添字照合バグの検出)
            Assert.NotEqual(0f, mid.intensity, 4);
        }

        [Fact]
        public void Bool値とInt値は区間開始値になる()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.highQuality = false;
            end.highQuality = true;
            start.blurIterations = 1;
            end.blurIterations = 9;
            start.visible = false;
            end.visible = true;

            var mid = Lerp(start, end, 0.9f);

            Assert.False(mid.highQuality);
            Assert.Equal(1, mid.blurIterations);
            Assert.False(mid.visible);
        }

        [Fact]
        public void 文字列値は区間開始値になる()
        {
            var start = new TransformDataModelMaterial();
            start.Initialize("mat");
            var end = new TransformDataModelMaterial();
            end.Initialize("mat");

            var scratch = (TransformDataModelMaterial)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, 0.9f);

            for (var i = 0; i < start.strValues.Length; i++)
            {
                Assert.Equal(start.strValues[i], scratch.strValues[i]);
            }
        }

        private static TransformDataRimlight CreateRimlight()
        {
            var trans = new TransformDataRimlight();
            trans.Initialize("Rimlight");
            return trans;
        }

        [Fact]
        public void Clone後のbaseValuesは複製先のValueDataを指す()
        {
            var start = CreateRimlight();
            // 複製前にキャッシュを作らせる (これが無いと不具合が再現しない)
            var _ = start.baseValues;

            var clone = (TransformDataRimlight)start.Clone();

            // Rimlight の baseValues は rotationValues = values[0..3]
            Assert.Same(clone.values[0], clone.baseValues[0]);
            Assert.NotSame(start.values[0], clone.baseValues[0]);
        }

        private static TransformDataRimlight LerpRimlight(
            TransformDataRimlight start, TransformDataRimlight end, float t)
        {
            var scratch = (TransformDataRimlight)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void リムライトの光源方向はクォータニオンで補間される()
        {
            var startRotation = QuaternionUtils.EulerToQuaternion(Vector3.zero);
            var endRotation = QuaternionUtils.EulerToQuaternion(new Vector3(90f, 40f, 20f));

            var start = CreateRimlight();
            var end = CreateRimlight();
            start.rotation = startRotation;
            end.rotation = endRotation;

            var mid = LerpRimlight(start, end, 0.25f);

            // 4 成分を独立に補間すると slerp とはずれる。まとめて slerp されることを固定する
            var expected = QuaternionUtils.Slerp(startRotation, endRotation, 0.25f);
            Assert.Equal(0f, Quaternion.Angle(expected, mid.rotation), 2);

            // Hold に誤分類されると区間開始値のままになる (移植時の退行の再発検出)
            Assert.NotEqual(0f, Quaternion.Angle(startRotation, mid.rotation), 2);
        }

        [Fact]
        public void リムライトの表示トグルは構造値の昇格に巻き込まれない()
        {
            var start = CreateRimlight();
            var end = CreateRimlight();
            start.visible = false;
            end.visible = true;

            var mid = LerpRimlight(start, end, 0.9f);

            Assert.False(mid.visible);
        }
    }
}
