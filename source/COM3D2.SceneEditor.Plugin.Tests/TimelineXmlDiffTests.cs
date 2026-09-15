using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineXmlDiffTests
    {
        private static TransformXml CreateTransform(string name, float x)
        {
            return new TransformXml
            {
                name = name,
                type = TransformType.Move,
                values = new float[] { x, 0f, 0f },
            };
        }

        private static FrameXml CreateFrame(int frameNo, float x)
        {
            return new FrameXml
            {
                frameNo = frameNo,
                bones = new List<BoneXml>
                {
                    new BoneXml { transform = CreateTransform("bone", x) },
                },
            };
        }

        private static TimelineLayerXml CreateLayer(string className, int slotNo, float x)
        {
            return new TimelineLayerXml
            {
                className = className,
                slotNo = slotNo,
                keyFrames = new List<FrameXml> { CreateFrame(0, x), CreateFrame(10, x) },
            };
        }

        private static TimelineXml CreateTimeline()
        {
            var xml = new TimelineXml { maxFrameNo = 100, frameRate = 30f, anmName = "test" };
            xml.layers.Add(CreateLayer("MotionTimelineLayer", 0, 1f));
            xml.layers.Add(CreateLayer("CameraTimelineLayer", 0, 2f));
            xml.layers.Add(CreateLayer("LightTimelineLayer", 0, 3f));
            xml.models.Add(new TimelineModelXml { name = "model1" });
            return xml;
        }

        [Fact]
        public void 同一内容なら部分適用可能で変更レイヤーは無い()
        {
            var diff = TimelineXmlDiff.Compute(CreateTimeline(), CreateTimeline());

            Assert.True(diff.canApplyPartially);
            Assert.Empty(diff.changedLayerIndices);
        }

        [Fact]
        public void キー値が変わったレイヤーだけ添字に載る()
        {
            var after = CreateTimeline();
            after.layers[1].keyFrames[1].bones[0].transform.values[0] = 99f;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 1 }, diff.changedLayerIndices);
        }

        [Fact]
        public void キー追加も変更レイヤーとして検出する()
        {
            var after = CreateTimeline();
            after.layers[2].keyFrames.Add(CreateFrame(20, 3f));

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 2 }, diff.changedLayerIndices);
        }

        [Fact]
        public void タンジェントの有無の違いを検出する()
        {
            var after = CreateTimeline();
            after.layers[0].keyFrames[0].bones[0].transform.inTangents = new float[] { 0f, 0f, 0f };

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.True(diff.canApplyPartially);
            Assert.Equal(new List<int> { 0 }, diff.changedLayerIndices);
        }

        [Fact]
        public void レイヤー追加は部分適用不可()
        {
            var after = CreateTimeline();
            after.layers.Add(CreateLayer("MoveTimelineLayer", 0, 4f));

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void レイヤーの並び替えは部分適用不可()
        {
            var after = CreateTimeline();
            var first = after.layers[0];
            after.layers[0] = after.layers[1];
            after.layers[1] = first;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void レイヤー外の設定変更は部分適用不可()
        {
            var after = CreateTimeline();
            after.maxFrameNo = 200;

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void モデル一覧の変更は部分適用不可()
        {
            var after = CreateTimeline();
            after.models.Add(new TimelineModelXml { name = "model2" });

            var diff = TimelineXmlDiff.Compute(CreateTimeline(), after);

            Assert.False(diff.canApplyPartially);
        }

        [Fact]
        public void どちらかがnullなら部分適用不可()
        {
            Assert.False(TimelineXmlDiff.Compute(null, CreateTimeline()).canApplyPartially);
            Assert.False(TimelineXmlDiff.Compute(CreateTimeline(), null).canApplyPartially);
        }

        [Fact]
        public void SerializeWithoutLayersは呼び出し後もlayersを保つ()
        {
            var xml = CreateTimeline();

            var text = TimelineXmlDiff.SerializeWithoutLayers(xml);

            Assert.Equal(3, xml.layers.Count);
            Assert.DoesNotContain("<Frame>", text);
            Assert.Contains("model1", text);
        }
    }
}
