using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// anm 出力の 1 フレーム調整が PlayDataBase.Setup と同じ規則でキーを潰すことを固定する
    /// </summary>
    public class AnmSingleFrameAdjusterTests
    {
        private const float Frame = 1f / 30f;
        private const float Eps = Frame * AnmSingleFrameAdjuster.EpsilonRatio;

        /// <summary>末尾をボーン全体の最後の実キー (isLast) として並べる</summary>
        private static List<AnmKeySource> Keys(params int[] frameNos)
        {
            var keys = new List<AnmKeySource>();
            for (var i = 0; i < frameNos.Length; i++)
            {
                keys.Add(new AnmKeySource
                {
                    frameNo = frameNos[i],
                    time = frameNos[i] * Frame,
                    isSynthetic = false,
                    isLast = i == frameNos.Length - 1,
                });
            }
            return keys;
        }

        private static void AssertUntouched(AnmKeyTiming[] result, List<AnmKeySource> keys)
        {
            Assert.Equal(keys.Count, result.Length);
            for (var i = 0; i < keys.Count; i++)
            {
                Assert.Equal(keys[i].time, result[i].time);
                Assert.False(result[i].stepIn);
                Assert.False(result[i].stepOut);
            }
        }

        [Fact]
        public void Noneは入力をそのまま返す()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.None, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void 一フレーム差が無ければ変更しない()
        {
            var keys = Keys(0, 10, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void Delayは前キーを次キー直前へ後ろ倒しステップ化する()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);

            Assert.Equal(11 * Frame - Eps, result[1].time, 6);
            Assert.Equal(11 * Frame, result[2].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
            Assert.False(result[1].stepIn);
            Assert.False(result[2].stepOut);
            Assert.Equal(0f, result[0].time);
            Assert.Equal(20 * Frame, result[3].time);
        }

        [Fact]
        public void Advanceは次キーを前キー位置へ前倒しステップ化する()
        {
            var keys = Keys(0, 10, 11, 20);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);

            Assert.Equal(10 * Frame - Eps, result[1].time, 6);
            Assert.Equal(10 * Frame, result[2].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
        }

        [Fact]
        public void 最後の区間は潰さない()
        {
            var keys = Keys(0, 10, 11);
            var delay = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(delay, keys);
            var advance = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            AssertUntouched(advance, keys);
        }

        [Fact]
        public void 窓の末尾でもボーン全体の最後でなければ潰す()
        {
            // activeTrack で切り出したプレビュー窓: 末尾ペアが 1 フレーム差でも真の終端ではない
            var keys = Keys(0, 10, 11);
            keys[2] = new AnmKeySource { frameNo = 11, time = 11 * Frame, isSynthetic = false, isLast = false };
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            Assert.Equal(11 * Frame - Eps, result[1].time, 6);
            Assert.True(result[1].stepOut);
            Assert.True(result[2].stepIn);
        }

        [Fact]
        public void 四連鎖でも単調増加とステップ化が保たれる()
        {
            var keys = Keys(5, 6, 7, 8, 20);
            foreach (var type in new[] { SingleFrameType.Delay, SingleFrameType.Advance })
            {
                var r = AnmSingleFrameAdjuster.Adjust(keys, type, Eps);
                for (var i = 1; i < r.Length; i++)
                {
                    Assert.True(r[i - 1].time < r[i].time, type + ": 時刻が単調増加でない: " + i);
                }
                for (var i = 0; i < 3; i++)
                {
                    Assert.True(r[i].stepOut, type + ": stepOut " + i);
                    Assert.True(r[i + 1].stepIn, type + ": stepIn " + (i + 1));
                }
                Assert.False(r[4].stepIn);
            }
        }

        [Fact]
        public void 合成キーを含むペアは潰さない()
        {
            var keys = Keys(0, 1, 10);
            keys[0] = new AnmKeySource { frameNo = 0, time = 0f, isSynthetic = true, isLast = false };
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            AssertUntouched(result, keys);
        }

        [Fact]
        public void 連鎖はイプシロン刻みで狭義単調増加になる()
        {
            var keys = Keys(0, 10, 11, 12, 20);

            var delay = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Delay, Eps);
            Assert.Equal(11 * Frame - Eps, delay[1].time, 6);
            Assert.Equal(12 * Frame - Eps, delay[2].time, 6);
            Assert.Equal(12 * Frame, delay[3].time, 6);
            Assert.True(delay[1].stepOut && delay[2].stepIn && delay[2].stepOut && delay[3].stepIn);

            var advance = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            Assert.Equal(10 * Frame - 2 * Eps, advance[1].time, 6);
            Assert.Equal(10 * Frame - Eps, advance[2].time, 6);
            Assert.Equal(10 * Frame, advance[3].time, 6);

            foreach (var r in new[] { delay, advance })
            {
                for (var i = 1; i < r.Length; i++)
                {
                    Assert.True(r[i - 1].time < r[i].time, "時刻が単調増加でない: " + i);
                }
            }
        }

        [Fact]
        public void 先頭ペアのAdvanceは負時刻を許す()
        {
            var keys = Keys(0, 1, 10);
            var result = AnmSingleFrameAdjuster.Adjust(keys, SingleFrameType.Advance, Eps);
            Assert.Equal(-Eps, result[0].time, 6);
            Assert.Equal(0f, result[1].time, 6);
            Assert.True(result[0].stepOut);
            Assert.True(result[1].stepIn);
        }

        [Fact]
        public void キーが1つ以下なら空か同値()
        {
            Assert.Empty(AnmSingleFrameAdjuster.Adjust(new List<AnmKeySource>(), SingleFrameType.Delay, Eps));
            var one = Keys(5);
            AssertUntouched(AnmSingleFrameAdjuster.Adjust(one, SingleFrameType.Delay, Eps), one);
        }
    }
}
