using System;
using System.IO;
using System.Xml.Serialization;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// キー割り当ては SceneEditor.xml (SE 本体の Config) が唯一の持ち主。
    /// 既定の取りこぼしは実行時の KeyNotFoundException になるためここで固定する
    /// </summary>
    public class KeyBindConfigTests
    {
        [Fact]
        public void 全てのKeyBindTypeに既定の割り当てがある()
        {
            var config = new Config();

            foreach (KeyBindType type in Enum.GetValues(typeof(KeyBindType)))
            {
                Assert.True(
                    config.keyBinds.ContainsKey(type),
                    "既定の割り当てがありません: " + type);
            }
        }

        [Fact]
        public void タイムライン操作のキーがSceneEditor側にある()
        {
            var config = new Config();

            // KeyBind.ToString は KeyCode.Return を "Enter" として返す
            Assert.Equal("Enter", config.GetKeyName(KeyBindType.AddKeyFrame));
            Assert.Equal("Shift+Enter", config.GetKeyName(KeyBindType.AddKeyFrameAll));
            Assert.Equal("Backspace", config.GetKeyName(KeyBindType.RemoveKeyFrame));
            Assert.Equal("Space", config.GetKeyName(KeyBindType.Play));
            Assert.Equal("Shift", config.GetKeyName(KeyBindType.MultiSelect));
        }

        [Fact]
        public void 旧TimelineXmlのkeyBind要素は読み飛ばして他の項目を復元する()
        {
            // クラスから削除済みの項目。既存ユーザーの Timeline.xml には残っている
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<keyBind><key>AddKeyFrame</key><value>Return</value></keyBind>"
                + "<keyBind><key>EditMode</key><value>F1</value></keyBind>"
                + "<disablePoseHistory>false</disablePoseHistory>"
                + "<voiceMaxLength>12.5</voiceMaxLength>"
                + "</Config>";

            var serializer = new XmlSerializer(typeof(MTEP.Config));
            using (var reader = new StringReader(xml))
            {
                var config = (MTEP.Config) serializer.Deserialize(reader);

                Assert.Equal(12.5f, config.voiceMaxLength);
            }
        }
    }
}
