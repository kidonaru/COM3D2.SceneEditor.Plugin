using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>1 フレーム調整で区間が潰れるかの判定を固定する</summary>
    public class SingleFrameIntervalTests
    {
        private class FakeMotion : IMotionData
        {
            public int stFrame { get; set; }
            public int edFrame { get; set; }
            public int stFrameInEdit { get; set; }
            public int edFrameInEdit { get; set; }
            public int stFrameActive => stFrame;
            public int edFrameActive => edFrame;
        }

        [Theory]
        [InlineData(SingleFrameType.Delay)]
        [InlineData(SingleFrameType.Advance)]
        public void 一フレーム差の区間は潰れる(SingleFrameType type)
        {
            Assert.True(SingleFrameInterval.IsCollapsed(type, 10, 11, false));
        }

        [Fact]
        public void Noneでは潰れない()
        {
            Assert.False(SingleFrameInterval.IsCollapsed(SingleFrameType.None, 10, 11, false));
        }

        [Fact]
        public void 二フレーム以上離れた区間は潰れない()
        {
            Assert.False(SingleFrameInterval.IsCollapsed(SingleFrameType.Delay, 10, 12, false));
        }

        [Fact]
        public void 最後の区間は潰れない()
        {
            Assert.False(SingleFrameInterval.IsCollapsed(SingleFrameType.Delay, 10, 11, true));
        }

        [Fact]
        public void キー列では末尾の区間だけ最後の区間として扱う()
        {
            var frameNos = new List<int> { 0, 1, 2, 10, 11 };
            var type = SingleFrameType.Delay;

            Assert.True(SingleFrameInterval.IsCollapsed(type, frameNos, 0));
            Assert.True(SingleFrameInterval.IsCollapsed(type, frameNos, 1));
            Assert.False(SingleFrameInterval.IsCollapsed(type, frameNos, 2));
            Assert.False(SingleFrameInterval.IsCollapsed(type, frameNos, 3));
        }

        [Fact]
        public void キー列の範囲外の区間は潰れない()
        {
            var frameNos = new List<int> { 0, 1, 10 };
            Assert.False(SingleFrameInterval.IsCollapsed(SingleFrameType.Delay, frameNos, -1));
            Assert.False(SingleFrameInterval.IsCollapsed(SingleFrameType.Delay, frameNos, 2));
        }

        private static PlayDataBase<FakeMotion> SetupPlayData(int[] frames, SingleFrameType type)
        {
            var playData = new PlayDataBase<FakeMotion>();
            for (var i = 0; i < frames.Length - 1; i++)
            {
                playData.motions.Add(new FakeMotion { stFrame = frames[i], edFrame = frames[i + 1] });
            }
            playData.Setup(type);
            return playData;
        }

        public static IEnumerable<object[]> FrameLists => new[]
        {
            new object[] { new[] { 0, 10, 11, 20 } },
            new object[] { new[] { 0, 1, 10 } },
            new object[] { new[] { 0, 5, 10, 11 } },
            new object[] { new[] { 0, 5, 6, 7, 8, 9, 20 } },
            new object[] { new[] { 3, 4, 5, 6 } },
        };

        [Theory]
        [MemberData(nameof(FrameLists))]
        public void Delayで始端が動く区間と判定が一致する(int[] frames)
        {
            // Delay の Setup は潰した区間の始端だけを終端へ動かす (後続の対は終端しか動かさない)
            var playData = SetupPlayData(frames, SingleFrameType.Delay);

            var frameNos = new List<int>(frames);
            for (var i = 0; i < playData.motions.Count; i++)
            {
                var moved = playData.motions[i].stFrame != frames[i];
                Assert.Equal(moved, SingleFrameInterval.IsCollapsed(SingleFrameType.Delay, frameNos, i));
            }
        }

        [Theory]
        [MemberData(nameof(FrameLists))]
        public void Advanceで終端が動く区間と判定が一致する(int[] frames)
        {
            // Advance の Setup は潰した区間の終端だけを始端へ動かす (後続の対は始端しか動かさない)
            var playData = SetupPlayData(frames, SingleFrameType.Advance);

            var frameNos = new List<int>(frames);
            for (var i = 0; i < playData.motions.Count; i++)
            {
                var moved = playData.motions[i].edFrame != frames[i + 1];
                Assert.Equal(moved, SingleFrameInterval.IsCollapsed(SingleFrameType.Advance, frameNos, i));
            }
        }
    }
}
