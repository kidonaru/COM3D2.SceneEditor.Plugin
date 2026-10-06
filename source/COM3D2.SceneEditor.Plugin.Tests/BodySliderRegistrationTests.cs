using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型項目のタイムライン登録 (どの項目を BoneMenu に出してキーにするか) の保存と旧 XML の補完を固定する</summary>
    public class BodySliderRegistrationTests
    {
        private static TransformXml CreateKey(string name, TransformType type, float[] values)
        {
            return new TransformXml { name = name, type = type, values = values };
        }

        private static TimelineXml CreateTimeline(int version, string className, int slotNo, params TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml { transform = transform });
            }

            var layer = new TimelineLayerXml { className = className, slotNo = slotNo };
            layer.keyFrames.Add(new FrameXml { frameNo = 0, bones = bones });

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        private static string[] Registered(TimelineXml xml, int slotNo)
        {
            return xml.maidBodySliderKeys
                .Where(k => k.maidSlotNo == slotNo)
                .Select(k => k.key)
                .OrderBy(k => k)
                .ToArray();
        }

        [Fact]
        public void 登録の無い旧XMLは体型レイヤーにキーのある項目を登録する()
        {
            var timeline = CreateTimeline(39, "BodySliderTimelineLayer", 1,
                CreateKey("THISCL", TransformType.BodySlider, new[] { 1.2f, 1f, 1f }),
                CreateKey("MUNEPOS", TransformType.BodySlider, new[] { 0f, 0.1f, 0f }));

            timeline.Initialize();

            Assert.Equal(new[] { "MUNEPOS", "THISCL" }, Registered(timeline, 1));
            Assert.Empty(Registered(timeline, 0));
        }

        [Fact]
        public void 登録済みの項目は重複させない()
        {
            var timeline = CreateTimeline(39, "BodySliderTimelineLayer", 0,
                CreateKey("THISCL", TransformType.BodySlider, new[] { 1.2f, 1f, 1f }));
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 0, key = "THISCL" });
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 0, key = "SPIPOS" });

            timeline.Initialize();

            Assert.Equal(new[] { "SPIPOS", "THISCL" }, Registered(timeline, 0));
        }

        [Fact]
        public void メイドスケールから移行した腕の項目も登録する()
        {
            var timeline = CreateTimeline(38, "MaidScaleTimelineLayer", 0,
                CreateKey("Bip01 L Hand", TransformType.MaidScale, new[] { 1.5f }),
                CreateKey("Bip01 R UpperArm", TransformType.MaidScale, new[] { 1f }));

            timeline.Initialize();

            Assert.Equal(new[] { "HANDSCL_L", "UPARMSCL_R" }, Registered(timeline, 0));
        }

        [Fact]
        public void 他のレイヤーと未知の項目名は登録しない()
        {
            var timeline = CreateTimeline(39, "GravityTimelineLayer", 0,
                CreateKey("THISCL", TransformType.Gravity, new[] { 0f }));
            timeline.layers.Add(new TimelineLayerXml { className = "BodySliderTimelineLayer", slotNo = 0 });
            timeline.layers[1].keyFrames.Add(new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = CreateKey("UNKNOWN", TransformType.BodySlider, new[] { 1f, 1f, 1f }) } },
            });

            timeline.Initialize();

            Assert.Empty(timeline.maidBodySliderKeys);
        }

        [Fact]
        public void 登録はXMLの往復で保たれ空なら書き出さない()
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));

            var empty = new TimelineXml();
            string emptyText;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, empty);
                emptyText = writer.ToString();
            }
            Assert.DoesNotContain("MaidBodySliderKeys", emptyText);

            var timeline = new TimelineXml();
            timeline.maidBodySliderKeys.Add(new TimelineMaidBodySliderKeyXml { maidSlotNo = 2, key = "HANDSCL_L" });
            string text;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, timeline);
                text = writer.ToString();
            }
            Assert.Contains("<MaidBodySliderKeys>", text);

            TimelineXml loaded;
            using (var reader = new StringReader(text))
            {
                loaded = (TimelineXml)serializer.Deserialize(reader);
            }
            Assert.Single(loaded.maidBodySliderKeys);
            Assert.Equal(2, loaded.maidBodySliderKeys[0].maidSlotNo);
            Assert.Equal("HANDSCL_L", loaded.maidBodySliderKeys[0].key);
        }
    }
}
