using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 表情レイヤーのタンジェント補間フラグの既定値と XML 往復を固定する。
    /// 旧 XML には要素が無いので OFF、新規作成は ON でなければならない
    /// </summary>
    public class FaceTangentSettingTests
    {
        [Fact]
        public void 新規タイムラインは表情タンジェントONである()
        {
            Assert.True(new TimelineData().isTangentFace);
        }

        [Fact]
        public void 要素の無い旧XMLを読むとOFFになる()
        {
            var xml = new TimelineXml();
            Assert.False(xml.isTangentFace);

            var data = new TimelineData();
            data.FromXml(xml);
            Assert.False(data.isTangentFace);
        }

        [Fact]
        public void 表情タンジェントはXML往復で保持される()
        {
            var data = new TimelineData { isTangentFace = true };
            var xml = data.ToXml();
            Assert.True(xml.isTangentFace);

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TimelineXml));
            TimelineXml dst;
            using (var ms = new System.IO.MemoryStream())
            {
                serializer.Serialize(ms, xml);
                ms.Position = 0;
                dst = (TimelineXml)serializer.Deserialize(ms);
            }
            Assert.True(dst.isTangentFace);

            var loaded = new TimelineData();
            loaded.FromXml(dst);
            Assert.True(loaded.isTangentFace);
        }

        [Fact]
        public void タイムライン未ロードではモーフのタンジェントは無効()
        {
            // テスト環境では TimelineManager.instance.timeline が null。例外を出さず false
            var trans = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            Assert.False(trans.hasTangent);
            Assert.False(trans.ShouldSerializeInTangents());
        }

        [Fact]
        public void モーフのタンジェント対象は値スロットである()
        {
            var trans = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            Assert.Single(trans.tangentValues);
            Assert.Same(trans.morphValueValue, trans.tangentValues[0]);
        }

        [Fact]
        public void 全タンジェントが0で非スムーズなら未編集とみなす()
        {
            var a = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            var b = TimelineManager.CreateTransform<TransformDataMorph>("eyeclose");
            foreach (var trans in new[] { a, b })
            {
                foreach (var value in trans.tangentValues)
                {
                    value.inTangent.normalizedValue = 0f;
                    value.inTangent.isSmooth = false;
                    value.outTangent.normalizedValue = 0f;
                    value.outTangent.isSmooth = false;
                }
            }
            Assert.True(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));

            b.tangentValues[0].outTangent.normalizedValue = 0.5f;
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));

            b.tangentValues[0].outTangent.normalizedValue = 0f;
            b.tangentValues[0].inTangent.isSmooth = true;
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[] { a, b }));
        }

        [Fact]
        public void 空の列は未編集扱いにしない()
        {
            Assert.False(FaceTangentToggle.IsUntouched(new ITransformData[0]));
        }
    }
}
