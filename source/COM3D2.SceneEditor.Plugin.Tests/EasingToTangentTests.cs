using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using MTEPluginUtils = COM3D2.MotionTimelineEditor.Plugin.PluginUtils;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class EasingToTangentTests
    {
        [Fact]
        public void Linear変換_線形タンジェント()
        {
            var pair = EasingToTangent.Convert(MoveEasingType.Linear);
            Assert.Equal(1f, pair.outTangent, 2);
            Assert.Equal(1f, pair.inTangent, 2);
        }

        [Fact]
        public void QuadIn変換_始端フラット()
        {
            // f(t)=t^2 → f'(0)=0, f'(1)=2
            var pair = EasingToTangent.Convert(MoveEasingType.QuadIn);
            Assert.Equal(0f, pair.outTangent, 1);
            Assert.Equal(2f, pair.inTangent, 1);
        }

        // 実測の最大誤差 (全域 101 点サンプリング):
        //   Quad/Cubic の In・Out は 3 次で厳密表現できるためほぼ 0
        //   Sine 系 0.010、Quart/Quint/Exp/Circ の In・Out は 0.012〜0.084
        //   InOut 系は端点勾配 0 の 3 次では中腹の急峻さを表現できず 0.037〜0.229
        [Theory]
        [InlineData(MoveEasingType.QuadOut, 0.001f)]
        [InlineData(MoveEasingType.CubicIn, 0.001f)]
        [InlineData(MoveEasingType.SineInOut, 0.02f)]
        [InlineData(MoveEasingType.QuintIn, 0.05f)]
        [InlineData(MoveEasingType.ExpIn, 0.1f)]
        [InlineData(MoveEasingType.CircIn, 0.15f)]
        [InlineData(MoveEasingType.CubicInOut, 0.12f)]
        public void 単調Easingの近似誤差が閾値以下(MoveEasingType easing, float allowedError)
        {
            var pair = EasingToTangent.Convert(easing);
            // 変換後 Hermite と元 easing の最大誤差を全域サンプリングで検証
            float maxError = MaxError(easing, pair);
            Assert.True(maxError < allowedError, $"maxError={maxError}");
        }

        [Fact]
        public void 全Easing種で形状が破綻しない()
        {
            // 端点微分をそのまま使う実装では Circ 系が 1.0 まで暴れる。
            // 最小二乗フィットにより全種で 0.23 以内に収まることを担保する
            for (int i = 0; i < (int)MoveEasingType.Max; i++)
            {
                var easing = (MoveEasingType)i;
                var maxError = MaxError(easing, EasingToTangent.Convert(easing));
                Assert.True(maxError < 0.23f, $"{easing}: maxError={maxError}");
            }
        }

        private static float MaxError(MoveEasingType easing, TangentPair pair)
        {
            float maxError = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float expected = EasingFunctions.MoveEasing(t, easing);
                float actual = MTEPluginUtils.HermiteSimplified(pair.outTangent, pair.inTangent, t);
                maxError = System.Math.Max(maxError, System.Math.Abs(expected - actual));
            }
            return maxError;
        }

        [Fact]
        public void 全Easing種で変換が発散しない()
        {
            for (int i = 0; i < (int)MoveEasingType.Max; i++)
            {
                var pair = EasingToTangent.Convert((MoveEasingType)i);
                Assert.False(float.IsNaN(pair.outTangent) || float.IsInfinity(pair.outTangent));
                Assert.False(float.IsNaN(pair.inTangent) || float.IsInfinity(pair.inTangent));
                Assert.InRange(pair.outTangent, 0f, 10f);
                Assert.InRange(pair.inTangent, 0f, 10f);
            }
        }
    }
}
