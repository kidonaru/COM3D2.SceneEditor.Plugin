using System;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using MTEPluginUtils = COM3D2.MotionTimelineEditor.Plugin.PluginUtils;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class InterpolationTests
    {
        public static TheoryData<MoveEasingType> AllEasingTypes()
        {
            var data = new TheoryData<MoveEasingType>();
            foreach (var type in Enum.GetValues(typeof(MoveEasingType)).Cast<MoveEasingType>())
                if (type != MoveEasingType.Max)
                    data.Add(type);
            return data;
        }

        [Theory]
        [MemberData(nameof(AllEasingTypes))]
        public void Easingは端点で0と1を返す(MoveEasingType type)
        {
            // Exp 系は 2^-10 ≒ 0.001 のオフセットを持つ標準実装 (MTE 互換) のため誤差 2 桁で判定
            Assert.Equal(0f, EasingFunctions.MoveEasing(0f, type), 2);
            Assert.Equal(1f, EasingFunctions.MoveEasing(1f, type), 2);
        }

        [Theory]
        [MemberData(nameof(AllEasingTypes))]
        public void Easingの中間値は0から1の範囲内(MoveEasingType type)
        {
            for (var t = 0.1f; t < 1f; t += 0.1f)
            {
                var v = EasingFunctions.MoveEasing(t, type);
                Assert.InRange(v, -0.001f, 1.001f);
            }
        }

        [Fact]
        public void Linearは恒等写像()
        {
            Assert.Equal(0.5f, EasingFunctions.Linear(0.5f), 5);
            Assert.Equal(0.25f, EasingFunctions.Linear(0.25f), 5);
        }

        [Fact]
        public void Hermiteは端点で始点値と終点値を返す()
        {
            Assert.Equal(2f, MTEPluginUtils.Hermite(0f, 1f, 2f, 5f, 0.3f, -0.2f, 0f), 5);
            Assert.Equal(5f, MTEPluginUtils.Hermite(0f, 1f, 2f, 5f, 0.3f, -0.2f, 1f), 5);
        }

        [Fact]
        public void Hermiteはタンジェント0で中点が平均値になる()
        {
            // タンジェント 0 の 3 次エルミートは smoothstep。t=0.5 で中間値
            Assert.Equal(3.5f, MTEPluginUtils.Hermite(0f, 1f, 2f, 5f, 0f, 0f, 0.5f), 5);
        }

        [Fact]
        public void Hermiteは時間差ゼロなら開始値を返す()
        {
            Assert.Equal(2f, MTEPluginUtils.Hermite(1f, 1f, 2f, 5f, 0.3f, -0.2f, 0.5f), 5);
        }

        [Fact]
        public void HermiteSimplifiedは線形タンジェントで恒等写像()
        {
            // outTangent = inTangent = 1 (傾き 1) のエルミートは線形補間と一致する
            for (var t = 0f; t <= 1f; t += 0.25f)
                Assert.Equal(t, MTEPluginUtils.HermiteSimplified(1f, 1f, t), 5);
        }
    }
}
