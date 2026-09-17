using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 36 での背景レイヤー移行 (背景ごとのボーン → 固定ボーン BG + strValues[0] に背景名) を固定する。
    /// 旧タイムラインと MTE 産タイムラインはどちらもこの経路で読むため、ずれると背景キーが黙って消える
    /// </summary>
    public class BGLayerBoneMigrationTests
    {
        private static TransformXml CreateBgTransform(string bgName)
        {
            return new TransformXml
            {
                name = bgName,
                type = TransformType.BG,
                values = new float[9],
            };
        }

        private static TimelineXml CreateTimeline(int version, string className, params TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml { transform = transform });
            }

            // FrameXml.bones は既定が null なので明示的に作る
            var keyFrame = new FrameXml { frameNo = 0, bones = bones };
            var layer = new TimelineLayerXml { className = className };
            layer.keyFrames.Add(keyFrame);

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        [Fact]
        public void Initialize_v35の背景ボーンは名前がBGになり背景名がstrValuesへ移る()
        {
            var timeline = CreateTimeline(35, "BGTimelineLayer", CreateBgTransform("Yashiki_Day"));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal("BG", transform.name);
            Assert.Equal(new[] { "Yashiki_Day" }, transform.strValues);
        }

        [Fact]
        public void Initialize_背景なしのボーンは空文字の背景名になる()
        {
            var timeline = CreateTimeline(35, "BGTimelineLayer", CreateBgTransform(""));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal("BG", transform.name);
            Assert.Equal(new[] { "" }, transform.strValues);
        }

        [Fact]
        public void Initialize_同一フレームに複数ボーンがあれば最後だけ残る()
        {
            var timeline = CreateTimeline(35, "BGTimelineLayer",
                CreateBgTransform("First"), CreateBgTransform("Last"));

            timeline.Initialize();

            var bones = timeline.layers[0].keyFrames[0].bones;
            Assert.Single(bones);
            Assert.Equal(new[] { "Last" }, bones[0].transform.strValues);
        }

        [Fact]
        public void Initialize_v36以降は変換しない()
        {
            var transform = CreateBgTransform("BG");
            transform.strValues = new[] { "Yashiki_Day" };
            var timeline = CreateTimeline(36, "BGTimelineLayer", transform);

            timeline.Initialize();

            Assert.Equal(new[] { "Yashiki_Day" }, timeline.layers[0].keyFrames[0].bones[0].transform.strValues);
        }

        [Fact]
        public void Initialize_背景レイヤー以外のボーン名は変えない()
        {
            var timeline = CreateTimeline(35, "BGColorTimelineLayer", CreateBgTransform("BGColor"));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal("BGColor", transform.name);
            Assert.Null(transform.strValues);
        }
    }
}
