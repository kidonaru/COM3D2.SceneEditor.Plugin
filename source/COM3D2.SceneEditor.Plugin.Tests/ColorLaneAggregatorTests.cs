using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ColorLaneAggregatorTests
    {
        private static readonly Color Red = new Color(1f, 0f, 0f, 1f);
        private static readonly Color Blue = new Color(0f, 0f, 1f, 1f);

        private static Color White(float alpha)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        private static List<ColorLaneSegment> Build(ColorLaneAggregator aggregator)
        {
            var result = new List<ColorLaneSegment>();
            aggregator.BuildSegments(result);
            return result;
        }

        private static void AssertSegment(
            ColorLaneSegment segment, int start, int end, bool isLast, Color from, Color to)
        {
            Assert.Equal(start, segment.startFrameNo);
            Assert.Equal(end, segment.endFrameNo);
            Assert.Equal(isLast, segment.isLast);
            Assert.Equal(from, segment.fromColor);
            Assert.Equal(to, segment.toColor);
        }

        [Fact]
        public void 単一トラックはキー間を区間にして最後を末尾まで伸ばす()
        {
            var aggregator = new ColorLaneAggregator();
            var track = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(track, 0, Red, hidden: false);
            aggregator.AddKey(track, 30, Blue, hidden: false);

            var segments = Build(aggregator);

            Assert.Equal(2, segments.Count);
            AssertSegment(segments[0], 0, 30, false, Red, Blue);
            AssertSegment(segments[1], 30, 30, true, Blue, Blue);
        }

        [Fact]
        public void 段階表示のトラックは区間開始の色で塗る()
        {
            var aggregator = new ColorLaneAggregator();
            var track = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
            aggregator.AddKey(track, 0, Color.white, hidden: false);
            aggregator.AddKey(track, 30, Color.clear, hidden: false);

            var segments = Build(aggregator);

            AssertSegment(segments[0], 0, 30, false, Color.white, Color.white);
        }

        [Fact]
        public void 非表示のキーから次のキーまでは区間を出さない()
        {
            var aggregator = new ColorLaneAggregator();
            var track = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(track, 0, Red, hidden: true);
            aggregator.AddKey(track, 30, Red, hidden: false);

            var segments = Build(aggregator);

            Assert.Single(segments);
            Assert.Equal(30, segments[0].startFrameNo);
        }

        [Fact]
        public void 複数トラックは全キー位置で区切り最も不透明な値を取る()
        {
            var aggregator = new ColorLaneAggregator();
            var a = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(a, 0, White(0f), hidden: false);
            aggregator.AddKey(a, 20, White(1f), hidden: false);
            var b = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(b, 0, White(0.8f), hidden: false);
            aggregator.AddKey(b, 10, White(0f), hidden: false);

            var segments = Build(aggregator);

            // 区切りは 0 / 10 / 20。a は 10 で 0.5 まで上がる
            Assert.Equal(3, segments.Count);
            AssertSegment(segments[0], 0, 10, false, White(0.8f), White(0.5f));
            AssertSegment(segments[1], 10, 20, false, White(0.5f), White(1f));
            AssertSegment(segments[2], 20, 20, true, White(1f), White(1f));
        }

        [Fact]
        public void 最初のキーより前のトラックは合成に加えない()
        {
            var aggregator = new ColorLaneAggregator();
            var a = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
            aggregator.AddKey(a, 0, Color.clear, hidden: false);
            var b = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
            aggregator.AddKey(b, 10, Color.white, hidden: false);

            var segments = Build(aggregator);

            AssertSegment(segments[0], 0, 10, false, Color.clear, Color.clear);
            AssertSegment(segments[1], 10, 10, true, Color.white, Color.white);
        }

        [Fact]
        public void 色の帯があれば先頭の1本だけを出す()
        {
            var aggregator = new ColorLaneAggregator();
            var onOff = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
            aggregator.AddKey(onOff, 0, Color.white, hidden: false);
            var first = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(first, 10, Red, hidden: false);
            var second = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(second, 5, Blue, hidden: false);

            var segments = Build(aggregator);

            Assert.Single(segments);
            AssertSegment(segments[0], 10, 10, true, Red, Red);
        }

        [Fact]
        public void 値の透明度の帯があればONOFFの帯は混ぜない()
        {
            var aggregator = new ColorLaneAggregator();
            var onOff = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
            aggregator.AddKey(onOff, 0, Color.white, hidden: false);
            var value = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(value, 0, White(0.25f), hidden: false);

            var segments = Build(aggregator);

            Assert.Single(segments);
            Assert.Equal(White(0.25f), segments[0].fromColor);
        }

        [Fact]
        public void 同じフレームのキーは1つの区切りにまとめる()
        {
            var aggregator = new ColorLaneAggregator();
            for (var i = 0; i < 2; i++)
            {
                var track = aggregator.AddTrack(LaneColorSource.Bool, isStep: true);
                aggregator.AddKey(track, 0, Color.white, hidden: false);
                aggregator.AddKey(track, 30, Color.clear, hidden: false);
            }

            var segments = Build(aggregator);

            Assert.Equal(2, segments.Count);
            AssertSegment(segments[0], 0, 30, false, Color.white, Color.white);
            AssertSegment(segments[1], 30, 30, true, Color.clear, Color.clear);
        }

        [Fact]
        public void 非表示のトラックは除いて他のトラックだけで合成する()
        {
            var aggregator = new ColorLaneAggregator();
            var hidden = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(hidden, 0, White(1f), hidden: true);
            aggregator.AddKey(hidden, 20, White(1f), hidden: false);
            var visible = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(visible, 0, White(0.25f), hidden: false);
            aggregator.AddKey(visible, 20, White(0.25f), hidden: false);

            var segments = Build(aggregator);

            AssertSegment(segments[0], 0, 20, false, White(0.25f), White(0.25f));
        }

        [Fact]
        public void 補間と段階表示のトラックが混ざると区切りの途中まで補間する()
        {
            var aggregator = new ColorLaneAggregator();
            var linear = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: false);
            aggregator.AddKey(linear, 0, White(0f), hidden: false);
            aggregator.AddKey(linear, 40, White(1f), hidden: false);
            var step = aggregator.AddTrack(LaneColorSource.ValueAlpha, isStep: true);
            aggregator.AddKey(step, 0, White(0.5f), hidden: false);
            aggregator.AddKey(step, 10, White(0f), hidden: false);

            var segments = Build(aggregator);

            // 0〜10 は段階表示の 0.5 が勝ち、10〜40 は補間トラックの 0.25→1
            AssertSegment(segments[0], 0, 10, false, White(0.5f), White(0.5f));
            AssertSegment(segments[1], 10, 40, false, White(0.25f), White(1f));
        }

        [Fact]
        public void 最後のキーが非表示なら末尾まで伸ばす区間を出さない()
        {
            var aggregator = new ColorLaneAggregator();
            var track = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(track, 0, Red, hidden: false);
            aggregator.AddKey(track, 30, Red, hidden: true);

            var segments = Build(aggregator);

            Assert.Single(segments);
            Assert.False(segments[0].isLast);
        }

        [Fact]
        public void Clearで前回のトラックを捨てる()
        {
            var aggregator = new ColorLaneAggregator();
            var track = aggregator.AddTrack(LaneColorSource.Color, isStep: false);
            aggregator.AddKey(track, 0, Red, hidden: false);

            aggregator.Clear();

            Assert.Equal(0, aggregator.trackCount);
            Assert.Empty(Build(aggregator));
        }
    }
}
