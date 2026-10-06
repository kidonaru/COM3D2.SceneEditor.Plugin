using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 39 でのメイドスケールレイヤー → 体型レイヤーの移行を固定する。
    /// 腕 6 本の均一倍率 s を、腕の左右別項目の (s, s, s) へ移す
    /// </summary>
    public class BodySliderMigrationTests
    {
        private static TransformXml CreateMaidScale(string boneName, float[] values)
        {
            return new TransformXml
            {
                name = boneName,
                type = TransformType.MaidScale,
                values = values,
                inTangents = values.Length > 0 ? new[] { 0.5f } : new float[0],
                outTangents = values.Length > 0 ? new[] { -0.5f } : new float[0],
                inSmoothBit = 1,
                outSmoothBit = 0,
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
        public void Initialize_v38のメイドスケールは体型レイヤーの腕の項目になる()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Forearm", new[] { 1.5f }));

            timeline.Initialize();

            var layer = timeline.layers[0];
            Assert.Equal("BodySliderTimelineLayer", layer.className);
            var transform = layer.keyFrames[0].bones[0].transform;
            Assert.Equal("FARMSCL_L", transform.name);
            Assert.Equal(TransformType.BodySlider, transform.type);
            Assert.Equal(new[] { 1.5f, 1.5f, 1.5f }, transform.values);
        }

        [Fact]
        public void Initialize_タンジェントとsmoothビットは3成分へ写る()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 R Hand", new[] { 2f }));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal("HANDSCL_R", transform.name);
            Assert.Equal(new[] { 0.5f, 0.5f, 0.5f }, transform.inTangents);
            Assert.Equal(new[] { -0.5f, -0.5f, -0.5f }, transform.outTangents);
            Assert.Equal(7L, transform.inSmoothBit);
            Assert.Equal(0L, transform.outSmoothBit);
        }

        [Fact]
        public void Initialize_値の無いキーは倍率1として移す()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new float[0]));

            timeline.Initialize();

            var transform = timeline.layers[0].keyFrames[0].bones[0].transform;
            Assert.Equal(new[] { 1f, 1f, 1f }, transform.values);
        }

        [Fact]
        public void Initialize_対象外の骨名のキーは捨てる()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 Head", new[] { 2f }),
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));

            timeline.Initialize();

            var bones = timeline.layers[0].keyFrames[0].bones;
            Assert.Single(bones);
            Assert.Equal("HANDSCL_L", bones[0].transform.name);
        }

        [Fact]
        public void Initialize_v39以降と他のレイヤーは変えない()
        {
            var current = CreateTimeline(39, "MaidScaleTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));
            current.Initialize();
            Assert.Equal("MaidScaleTimelineLayer", current.layers[0].className);

            var other = CreateTimeline(38, "GravityTimelineLayer",
                CreateMaidScale("Bip01 L Hand", new[] { 2f }));
            other.Initialize();
            Assert.Equal(TransformType.MaidScale, other.layers[0].keyFrames[0].bones[0].transform.type);
        }

        [Fact]
        public void 現行版は39()
        {
            Assert.Equal(39, TimelineData.CurrentVersion);
            Assert.Equal(39, TimelineXml.BodySliderVersion);
        }
    }
}
