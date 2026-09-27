using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインのシェーダー変更 (&lt;MaterialShaders&gt;) の保存と読込を固定する。
    /// 変更が無ければ書き出さず、要素の無い旧 XML は変更なしとして読む
    /// </summary>
    public class MaterialShaderXmlTests
    {
        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        private static TimelineMaterialShaderData Maid()
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = 0, owner = "wear", material = "Dress590_onep", index = 1,
                shader = "com3d2mod/Standard_NPRToonV2_Lit_",
            };
        }

        [Fact]
        public void 変更が無ければ書き出さない()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };

            Assert.DoesNotContain("MaterialShaders", Serialize(xml));
        }

        [Fact]
        public void 要素の無い旧XMLは変更なしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><TimelineData version=\"38\" />");

            Assert.Empty(restored.materialShaders);
        }

        [Fact]
        public void シェーダー変更を往復する()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(Maid().ToXml());

            var text = Serialize(xml);
            Assert.Contains("<Shader>com3d2mod/Standard_NPRToonV2_Lit_</Shader>", text);

            var restored = new TimelineMaterialShaderData();
            restored.FromXml(Deserialize(text).materialShaders[0]);
            Assert.True(Maid().ContentEquals(restored));
        }

        [Fact]
        public void 読込時に名前の欠けた項目は空文字で読む()
        {
            var restored = new TimelineMaterialShaderData();
            restored.FromXml(new TimelineMaterialShaderXml { maidSlotNo = -1 });

            Assert.Equal("", restored.owner);
            Assert.Equal("", restored.material);
            Assert.Equal("", restored.shader);
        }
    }
}
