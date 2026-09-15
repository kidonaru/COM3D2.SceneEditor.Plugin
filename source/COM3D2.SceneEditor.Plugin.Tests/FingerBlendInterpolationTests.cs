using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class FingerBlendInterpolationTests
    {
        private static TransformDataFingerBlend Create(float open, float fist, bool lock0, float lockOpen0)
        {
            var trans = new TransformDataFingerBlend();
            trans.Initialize("ArmFingerBlendR");
            trans.ValueOpen = open;
            trans.ValueFist = fist;
            trans.LockEnabled0 = lock0;
            trans.LockValue0 = new UnityEngine.Vector2(lockOpen0, 0f);
            return trans;
        }

        private static TransformDataFingerBlend Interpolate(
            TransformDataFingerBlend start, TransformDataFingerBlend end, float t)
        {
            var result = Create(0f, 0f, false, 0f);
            TransformDataFingerBlend.Interpolate(start, end, 0f, 1f, t, result);
            return result;
        }

        [Fact]
        public void タンジェント補間の対象になる()
        {
            var trans = Create(0f, 0f, false, 0f);
            Assert.True(trans.hasTangent);
            // ロック有効フラグ (bool) 5 個は対象外
            Assert.Equal(trans.valueCount - 5, trans.tangentValues.Length);
            // ValueData は値等価なので参照で突き合わせる
            Assert.DoesNotContain(trans.tangentValues, v => ReferenceEquals(v, trans.LockEnabled0Value));
            Assert.Contains(trans.tangentValues, v => ReferenceEquals(v, trans.ValueOpenValue));
        }

        [Fact]
        public void 中間フレームで開き閉じが補間される()
        {
            var start = Create(0f, 1f, false, 0f);
            var end = Create(1f, 0f, true, 1f);

            var mid = Interpolate(start, end, 0.5f);

            // タンジェント 0 のエルミートは t=0.5 で中点
            Assert.Equal(0.5f, mid.ValueOpen, 4);
            Assert.Equal(0.5f, mid.ValueFist, 4);
            Assert.Equal(0.5f, mid.LockValue0.x, 4);
        }

        [Fact]
        public void ロック有効フラグは開始キーの値をステップ適用する()
        {
            var start = Create(0f, 0f, false, 0f);
            var end = Create(0f, 0f, true, 0f);

            Assert.False(Interpolate(start, end, 0.9f).LockEnabled0);
        }

        [Fact]
        public void 端点では始点値と終点値を返す()
        {
            var start = Create(0.2f, 0.8f, false, 0f);
            var end = Create(0.7f, 0.1f, false, 0f);

            Assert.Equal(0.2f, Interpolate(start, end, 0f).ValueOpen, 4);
            Assert.Equal(0.7f, Interpolate(start, end, 1f).ValueOpen, 4);
            Assert.Equal(0.1f, Interpolate(start, end, 1f).ValueFist, 4);
        }
    }
}
