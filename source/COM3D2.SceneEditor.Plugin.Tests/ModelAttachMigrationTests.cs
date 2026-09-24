using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 37 でモデル単位のアタッチ (&lt;Models&gt;) をモデルキーへ移す移行を固定する。
    /// ずれると旧タイムラインのアタッチが読込で黙って外れる
    /// </summary>
    public class ModelAttachMigrationTests
    {
        private static TransformXml CreateModelKey(string name, TransformType type = TransformType.Model)
        {
            return new TransformXml
            {
                name = name,
                type = type,
                values = new float[TransformDataModel.LegacyValueCount],
            };
        }

        private static TimelineXml CreateTimeline(int version, TimelineModelXml model, params TransformXml[] transforms)
        {
            var layer = new TimelineLayerXml { className = "ModelTimelineLayer" };
            layer.keyFrames.Add(new FrameXml { frameNo = 0, bones = CreateBones(transforms) });
            // キーごとに別インスタンスにしないと、2 つ目のキーの変換漏れを検出できない
            layer.keyFrames.Add(new FrameXml { frameNo = 10, bones = CreateBones(transforms) });

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            timeline.models.Add(model);
            return timeline;
        }

        // FrameXml.bones は既定が null なので明示的に作る
        private static List<BoneXml> CreateBones(TransformXml[] transforms)
        {
            var bones = new List<BoneXml>();
            foreach (var transform in transforms)
            {
                bones.Add(new BoneXml
                {
                    transform = new TransformXml
                    {
                        name = transform.name,
                        type = transform.type,
                        values = (float[])transform.values.Clone(),
                    },
                });
            }
            return bones;
        }

        private static TimelineModelXml CreateModel(string name, AttachPoint point, int slot)
        {
            return new TimelineModelXml
            {
                name = name,
                attachPoint = point,
                attachMaidSlotNo = slot,
                pluginName = "ModItemExplorer",
            };
        }

        [Fact]
        public void Initialize_v36のモデル単位アタッチは同名モデルの全キーへ移る()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Hand_R, 1),
                CreateModelKey("cup.menu"));

            timeline.Initialize();

            foreach (var keyFrame in timeline.layers[0].keyFrames)
            {
                var values = keyFrame.bones[0].transform.values;
                Assert.Equal(15, values.Length);
                Assert.Equal(1f, values[(int)TransformDataModel.Index.AttachMaidSlotNo]);
                Assert.Equal((float)AttachPoint.Hand_R, values[(int)TransformDataModel.Index.AttachPoint]);
                Assert.Equal(0f, values[(int)TransformDataModel.Index.WorldLerp]);
            }
        }

        [Fact]
        public void Initialize_キー名がパス付きでもファイル名で対応付く()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Head, 0),
                CreateModelKey("menu/cup.menu"));

            timeline.Initialize();

            var values = timeline.layers[0].keyFrames[0].bones[0].transform.values;
            Assert.Equal((float)AttachPoint.Head, values[(int)TransformDataModel.Index.AttachPoint]);
        }

        [Fact]
        public void Initialize_アタッチなしのモデルと他の型のキーは変えない()
        {
            var timeline = CreateTimeline(36,
                CreateModel("cup.menu", AttachPoint.Null, -1),
                CreateModelKey("cup.menu"),
                CreateModelKey("cup.menu", TransformType.ModelBone));

            timeline.Initialize();

            var bones = timeline.layers[0].keyFrames[0].bones;
            Assert.Equal(TransformDataModel.LegacyValueCount, bones[0].transform.values.Length);
            Assert.Equal(TransformDataModel.LegacyValueCount, bones[1].transform.values.Length);
        }

        [Fact]
        public void Initialize_v37以降は変換しない()
        {
            var timeline = CreateTimeline(37,
                CreateModel("cup.menu", AttachPoint.Hand_R, 1),
                CreateModelKey("cup.menu"));

            timeline.Initialize();

            Assert.Equal(TransformDataModel.LegacyValueCount,
                timeline.layers[0].keyFrames[0].bones[0].transform.values.Length);
        }

        [Fact]
        public void モデル定義はアタッチを書き出さない()
        {
            var serializer = new XmlSerializer(typeof(TimelineModelXml));
            var writer = new StringWriter();
            serializer.Serialize(writer, CreateModel("cup.menu", AttachPoint.Hand_R, 1));

            var xml = writer.ToString();
            Assert.DoesNotContain("<AttachPoint>", xml);
            Assert.DoesNotContain("<AttachMaidSlotNo>", xml);
            Assert.Contains("<Name>cup.menu</Name>", xml);
        }

        [Fact]
        public void 現行バージョンは37()
        {
            Assert.Equal(37, TimelineData.CurrentVersion);
        }
    }
}
