using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TangentUnificationTests
    {
        private static ValueData[] NewValues(int count)
        {
            var values = new ValueData[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = new ValueData();
            }
            return values;
        }

        [Fact]
        public void QuadIn区間_開始側フラット_終端側2倍()
        {
            var prev = NewValues(3);
            var current = NewValues(3);

            TangentUnification.ApplyEasingTangent(prev, current, MoveEasingType.QuadIn);

            foreach (var value in prev)
            {
                Assert.Equal(0f, value.outTangent.normalizedValue, 2);
                Assert.False(value.outTangent.isSmooth);
            }
            foreach (var value in current)
            {
                Assert.Equal(2f, value.inTangent.normalizedValue, 2);
                Assert.False(value.inTangent.isSmooth);
            }
        }

        [Fact]
        public void Linear区間_正規化タンジェントは1()
        {
            var prev = NewValues(1);
            var current = NewValues(1);

            TangentUnification.ApplyEasingTangent(prev, current, MoveEasingType.Linear);

            // 既定値 0 のままだとフラット補間になるため、明示的に 1 が入ること
            Assert.Equal(1f, prev[0].outTangent.normalizedValue, 2);
            Assert.Equal(1f, current[0].inTangent.normalizedValue, 2);
        }

        [Fact]
        public void 線形タンジェントの明示書き込み()
        {
            var values = NewValues(2);

            TangentUnification.SetLinearTangent(values, isOut: true);

            foreach (var value in values)
            {
                Assert.Equal(1f, value.outTangent.normalizedValue, 2);
                Assert.False(value.outTangent.isSmooth);
                // 反対側は触らない
                Assert.Equal(0f, value.inTangent.normalizedValue, 2);
            }
        }

        [Fact]
        public void 再変換で編集済みタンジェントが上書きされない()
        {
            // Undo/Redo も ConvertTimeline を通るため、変換済みタイムラインでは
            // 2 回目以降が no-op でなければユーザーの編集値が Linear (1, 1) で潰れる
            var timeline = new TimelineData();
            Assert.False(timeline.isTangentUnified);

            TangentUnification.ConvertTimeline(timeline);
            Assert.True(timeline.isTangentUnified);

            // 変換済みフラグが立っていれば 2 回目は何もしない
            var prev = NewValues(1);
            var current = NewValues(1);
            prev[0].outTangent.normalizedValue = 2.5f;
            current[0].inTangent.normalizedValue = 0.25f;

            TangentUnification.ConvertTimeline(timeline);

            Assert.Equal(2.5f, prev[0].outTangent.normalizedValue, 2);
            Assert.Equal(0.25f, current[0].inTangent.normalizedValue, 2);
        }

        [Fact]
        public void 自動補間フラグは変換で解除される()
        {
            var prev = NewValues(1);
            var current = NewValues(1);
            prev[0].outTangent.isSmooth = true;
            current[0].inTangent.isSmooth = true;

            TangentUnification.ApplyEasingTangent(prev, current, MoveEasingType.SineInOut);

            Assert.False(prev[0].outTangent.isSmooth);
            Assert.False(current[0].inTangent.isSmooth);
        }
    }
}
