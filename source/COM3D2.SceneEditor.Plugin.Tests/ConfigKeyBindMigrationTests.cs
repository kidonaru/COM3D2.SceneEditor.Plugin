using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// SceneEditor.xml のキーバインド移行 (v1 → v2)。
    /// 編集モード切替を Tab から F1 へ移し、Tab をウィンドウ非表示に充てた変更で、
    /// 旧設定の Tab が二重割り当てにならないことを固定する
    /// </summary>
    public class ConfigKeyBindMigrationTests
    {
        private static Config Load(string xml)
        {
            var serializer = new XmlSerializer(typeof(Config));
            using (var reader = new StringReader(xml))
            {
                var config = (Config) serializer.Deserialize(reader);
                config.ConvertVersion();
                return config;
            }
        }

        [Fact]
        public void v1設定のTab編集モード切替はF1へ移りTabはウィンドウ非表示になる()
        {
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config version=\"1\">"
                + "<keyBind><key>EditModeToggle</key><value>Tab</value></keyBind>"
                + "</Config>";

            var config = Load(xml);

            Assert.Equal("F1", config.GetKeyName(KeyBindType.EditModeToggle));
            Assert.Equal("Tab", config.GetKeyName(KeyBindType.WindowsHiddenToggle));
            Assert.Equal(Config.CurrentVersion, config.version);
            Assert.True(config.dirty);
        }

        [Fact]
        public void v1設定でも編集モード切替をTab以外へ変えていれば維持される()
        {
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config version=\"1\">"
                + "<keyBind><key>EditModeToggle</key><value>E</value></keyBind>"
                + "</Config>";

            var config = Load(xml);

            Assert.Equal("E", config.GetKeyName(KeyBindType.EditModeToggle));
            Assert.Equal("Tab", config.GetKeyName(KeyBindType.WindowsHiddenToggle));
        }

        [Fact]
        public void v2設定は移行対象外で保存済みの値がそのまま残る()
        {
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config version=\"2\">"
                + "<keyBind><key>EditModeToggle</key><value>Tab</value></keyBind>"
                + "<keyBind><key>WindowsHiddenToggle</key><value>F2</value></keyBind>"
                + "</Config>";

            var config = Load(xml);

            Assert.Equal("Tab", config.GetKeyName(KeyBindType.EditModeToggle));
            Assert.Equal("F2", config.GetKeyName(KeyBindType.WindowsHiddenToggle));
            Assert.False(config.dirty);
        }
    }
}
