using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>表示トグルを足す前の Config を読んでも、すべて表示 (ON) から始まることを固定する</summary>
    public class ConfigViewToggleTests
    {
        [Fact]
        public void 表示トグルの無い旧設定は全部ONで読まれる()
        {
            var xml = "<?xml version=\"1.0\"?><Config version=\"2\"><sceneViewShowBg>false</sceneViewShowBg></Config>";
            Config config;
            using (var reader = new StringReader(xml))
            {
                config = (Config) new XmlSerializer(typeof(Config)).Deserialize(reader);
            }

            Assert.True(config.gameViewShowBg);
            Assert.True(config.gameViewShowMaid);
            Assert.True(config.gameViewShowModel);
            Assert.True(config.gameViewShowEffect);
            Assert.True(config.gameViewShowGizmo);
            Assert.True(config.sceneViewShowEffect);
            Assert.False(config.sceneViewShowBg);
        }

        [Fact]
        public void 表示トグルは保存して読み直せる()
        {
            var src = new Config { gameViewShowMaid = false, gameViewShowGizmo = false, sceneViewShowEffect = false };
            var serializer = new XmlSerializer(typeof(Config));
            var writer = new StringWriter();
            serializer.Serialize(writer, src);
            Config dst;
            using (var reader = new StringReader(writer.ToString()))
            {
                dst = (Config) serializer.Deserialize(reader);
            }

            Assert.False(dst.gameViewShowMaid);
            Assert.False(dst.gameViewShowGizmo);
            Assert.False(dst.sceneViewShowEffect);
            Assert.True(dst.gameViewShowBg);
        }
    }
}
