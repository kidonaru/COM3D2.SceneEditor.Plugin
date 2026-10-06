using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// シーンプリセットの重力のローカル（local 属性）の保存と読込を固定する。
    /// OFF は書き出さず、属性の無い旧プリセットは OFF（ワールド）として読む
    /// </summary>
    public class ScenePresetGravityXmlTests
    {
        private static string Serialize(ScenePresetGravity gravity)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetGravity));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, gravity);
                return writer.ToString();
            }
        }

        private static ScenePresetGravity Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetGravity));
            using (var reader = new StringReader(text))
            {
                return (ScenePresetGravity)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void OFFは書き出さない()
        {
            var gravity = new ScenePresetGravity { category = "skirt", enabled = true };

            Assert.DoesNotContain("local", Serialize(gravity));
        }

        [Fact]
        public void ONを往復する()
        {
            var gravity = new ScenePresetGravity
            {
                category = "skirt",
                enabled = true,
                local = true,
                offset = new Vector3(0f, -1f, 0f),
            };

            var text = Serialize(gravity);

            Assert.Contains("local=\"true\"", text);
            var restored = Deserialize(text);
            Assert.True(restored.local);
            Assert.True(restored.enabled);
            Assert.Equal(-1f, restored.offset.y);
        }

        [Fact]
        public void 属性の無い旧プリセットはOFFで読む()
        {
            var restored = Deserialize(
                "<ScenePresetGravity category=\"hair\" enabled=\"true\"><offset><x>0</x><y>-1</y><z>0</z></offset></ScenePresetGravity>");

            Assert.False(restored.local);
            Assert.True(restored.enabled);
        }
    }
}
