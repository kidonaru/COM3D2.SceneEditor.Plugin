using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// anm 出力の 1 フレーム調整が PlayDataBase.Setup と同じ区間表からキーを作ることを固定する
    /// </summary>
    public class AnmSingleFrameAdjusterTests
    {
        private const float Frame = 1f / 30f;
        private const float Eps = Frame * 0.01f;

        /// <summary>ボーン全体のキー列として並べる (末尾が isLast)</summary>
        private static List<AnmKeySource> Keys(params int[] frameNos)
        {
            var keys = new List<AnmKeySource>();
            for (var i = 0; i < frameNos.Length; i++)
            {
                keys.Add(new AnmKeySource
                {
                    frameNo = frameNos[i],
                    time = frameNos[i] * Frame,
                    boneIndex = i,
                    isSynthetic = false,
                    isLast = i == frameNos.Length - 1,
                });
            }
            return keys;
        }

        private static List<AnmKeyTiming> Adjust(List<AnmKeySource> keys, SingleFrameType type)
        {
            var result = new List<AnmKeyTiming>();
            AnmSingleFrameAdjuster.Adjust(keys, type, Frame, result);
            AssertStrictlyIncreasing(result);
            return result;
        }

        private static void AssertStrictlyIncreasing(List<AnmKeyTiming> result)
        {
            for (var i = 1; i < result.Count; i++)
            {
                Assert.True(result[i - 1].time < result[i].time, "時刻が狭義単調増加でない: " + i);
            }
        }

        private static void AssertUntouched(List<AnmKeyTiming> result, List<AnmKeySource> keys)
        {
            Assert.Equal(keys.Count, result.Count);
            for (var i = 0; i < keys.Count; i++)
            {
                Assert.Equal(i, result[i].source);
                Assert.Equal(keys[i].time, result[i].time);
                Assert.False(result[i].stepIn);
                Assert.False(result[i].stepOut);
            }
        }

        private static void AssertKey(AnmKeyTiming t, int source, float time, bool stepIn, bool stepOut)
        {
            Assert.Equal(source, t.source);
            Assert.Equal(time, t.time, 6);
            Assert.Equal(stepIn, t.stepIn);
            Assert.Equal(stepOut, t.stepOut);
        }

        [Fact]
        public void Noneは入力をそのまま返す()
        {
            var keys = Keys(0, 5, 10, 11, 20);
            AssertUntouched(Adjust(keys, SingleFrameType.None), keys);
        }

        [Fact]
        public void 一フレーム差が無ければ変更しない()
        {
            var keys = Keys(0, 10, 20);
            AssertUntouched(Adjust(keys, SingleFrameType.Delay), keys);
            AssertUntouched(Adjust(keys, SingleFrameType.Advance), keys);
        }

        [Fact]
        public void Delayは前区間を次キーまで延ばして次キーで切り替える()
        {
            var r = Adjust(Keys(0, 5, 10, 11, 20), SingleFrameType.Delay);

            Assert.Equal(5, r.Count);
            AssertKey(r[0], 0, 0f, false, false);
            AssertKey(r[1], 1, 5 * Frame, false, false);
            // 瞬間切替は公称時刻より ε 手前に置く。シーク位置 (rate * clip.length) が
            // キー時刻 (frame * fd - start * fd) より丸めで僅かに手前へ落ちても前キーの値を拾わない
            AssertKey(r[2], 2, 11 * Frame - 2 * Eps, false, true);
            AssertKey(r[3], 3, 11 * Frame - Eps, true, false);
            AssertKey(r[4], 4, 20 * Frame, false, false);
        }

        [Fact]
        public void Delayで2番目のキーの対は前区間を延ばさず保持する()
        {
            // Setup は i > 1 のときだけ前区間を延ばす
            var r = Adjust(Keys(0, 10, 11, 20), SingleFrameType.Delay);

            Assert.Equal(4, r.Count);
            AssertKey(r[1], 1, 10 * Frame, false, true);
            AssertKey(r[2], 2, 11 * Frame - Eps, true, false);
        }

        [Fact]
        public void Advanceは次キーを前キー位置へ前倒しして切り替える()
        {
            var r = Adjust(Keys(0, 10, 11, 20), SingleFrameType.Advance);

            Assert.Equal(4, r.Count);
            AssertKey(r[1], 1, 10 * Frame - 2 * Eps, false, true);
            AssertKey(r[2], 2, 10 * Frame - Eps, true, false);
            AssertKey(r[3], 3, 20 * Frame, false, false);
        }

        [Fact]
        public void 最後の区間は潰さない()
        {
            var keys = Keys(0, 5, 10, 11);
            AssertUntouched(Adjust(keys, SingleFrameType.Delay), keys);
            AssertUntouched(Adjust(keys, SingleFrameType.Advance), keys);
        }

        [Fact]
        public void 窓の末尾でもボーン全体の最後でなければ潰す()
        {
            // activeTrack で切り出したプレビュー窓: 末尾ペアが 1 フレーム差でも真の終端ではない
            var keys = Keys(0, 5, 10, 11);
            var last = keys[3];
            last.isLast = false;
            keys[3] = last;

            var delay = Adjust(keys, SingleFrameType.Delay);
            Assert.Equal(4, delay.Count);
            AssertKey(delay[2], 2, 11 * Frame - 2 * Eps, false, true);
            AssertKey(delay[3], 3, 11 * Frame - Eps, true, false);

            var advance = Adjust(keys, SingleFrameType.Advance);
            Assert.Equal(4, advance.Count);
            AssertKey(advance[2], 2, 10 * Frame - 2 * Eps, false, true);
            AssertKey(advance[3], 3, 10 * Frame - Eps, true, false);
        }

        [Fact]
        public void Delayの連鎖は1フレーム遅れで補間し最後の対だけ切り替える()
        {
            // Setup は連鎖の後続の対で前区間を延ばし直すので、連鎖内は補間が残る
            var r = Adjust(Keys(0, 5, 10, 11, 12, 20), SingleFrameType.Delay);

            Assert.Equal(6, r.Count);
            AssertKey(r[2], 2, 11 * Frame, false, false);
            AssertKey(r[3], 3, 12 * Frame - 2 * Eps, false, true);
            AssertKey(r[4], 4, 12 * Frame - Eps, true, false);
        }

        [Fact]
        public void Advanceの連鎖は連鎖末尾のキーへ一度に切り替える()
        {
            // Setup では連鎖途中のキーの区間はすべて長さ 0 になり、値として現れない
            var r = Adjust(Keys(0, 10, 11, 12, 20), SingleFrameType.Advance);

            Assert.Equal(new[] { 0, 1, 3, 4 }, r.Select(t => t.source).ToArray());
            AssertKey(r[1], 1, 10 * Frame - 2 * Eps, false, true);
            AssertKey(r[2], 3, 10 * Frame - Eps, true, false);
        }

        [Fact]
        public void 毎フレームキーのDelayは階段にならず補間が残る()
        {
            var r = Adjust(Keys(0, 5, 6, 7, 8, 9, 20), SingleFrameType.Delay);

            Assert.Equal(8, r.Count);
            // 2 番目のキー (5) の対は前区間を延ばさないので、5→6 は保持になる
            AssertKey(r[1], 1, 5 * Frame, false, true);
            AssertKey(r[2], 1, 6 * Frame - Eps, true, false);
            // 連鎖の途中 (5→6, 6→7) は 1 フレーム遅れで補間される
            AssertKey(r[3], 2, 7 * Frame, false, false);
            AssertKey(r[4], 3, 8 * Frame, false, false);
            AssertKey(r[5], 4, 9 * Frame - 2 * Eps, false, true);
            AssertKey(r[6], 5, 9 * Frame - Eps, true, false);
            AssertKey(r[7], 6, 20 * Frame, false, false);
        }

        [Fact]
        public void 合成キーを含むペアは潰さない()
        {
            var keys = Keys(0, 1, 10);
            keys[0] = new AnmKeySource { frameNo = 0, time = 0f, boneIndex = -1, isSynthetic = true };
            AssertUntouched(Adjust(keys, SingleFrameType.Delay), keys);
        }

        [Fact]
        public void 先頭ペアのDelayは先頭キーを保持して次キーで切り替える()
        {
            var r = Adjust(Keys(0, 1, 10), SingleFrameType.Delay);

            Assert.Equal(3, r.Count);
            AssertKey(r[0], 0, 0f, false, true);
            AssertKey(r[1], 1, 1 * Frame - Eps, true, false);
        }

        [Fact]
        public void 先頭ペアのAdvanceは負時刻を作らない()
        {
            var r = Adjust(Keys(0, 1, 10), SingleFrameType.Advance);

            Assert.Equal(new[] { 1, 2 }, r.Select(t => t.source).ToArray());
            AssertKey(r[0], 1, 0f, false, false);
        }

        [Fact]
        public void キーが1つ以下なら空か同値()
        {
            Assert.Empty(Adjust(new List<AnmKeySource>(), SingleFrameType.Delay));
            var one = Keys(5);
            AssertUntouched(Adjust(one, SingleFrameType.Delay), one);
        }
    }
}
