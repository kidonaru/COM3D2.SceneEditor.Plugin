using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ギズモの座標系設定の移行。3 値の gizmoSpace を後から足し、旧版互換の
    /// gizmoUseLocalSpace も残しているため、両者が食い違ったときの扱いを固定する
    /// </summary>
    public class ConfigGizmoSpaceMigrationTests
    {
        private static Config Load(string body)
        {
            var xml = "<?xml version=\"1.0\"?><Config version=\"2\">" + body + "</Config>";
            var serializer = new XmlSerializer(typeof(Config));
            using (var reader = new StringReader(xml))
            {
                var config = (Config) serializer.Deserialize(reader);
                config.ConvertVersion();
                return config;
            }
        }

        [Fact]
        public void gizmoSpaceの無い旧設定でGlobalならGlobalになる()
        {
            var config = Load("<gizmoUseLocalSpace>false</gizmoUseLocalSpace>");

            Assert.Equal(GizmoSpace.Global, config.gizmoSpace);
            Assert.False(config.gizmoUseLocalSpace);
            Assert.True(config.dirty);
        }

        [Fact]
        public void gizmoSpaceの無い旧設定でLocalならLocalのまま()
        {
            var config = Load("<gizmoUseLocalSpace>true</gizmoUseLocalSpace>");

            Assert.Equal(GizmoSpace.Local, config.gizmoSpace);
            Assert.False(config.dirty);
        }

        [Fact]
        public void Cameraとfalseの組は食い違いではないのでCameraを保つ()
        {
            var config = Load(
                "<gizmoUseLocalSpace>false</gizmoUseLocalSpace><gizmoSpace>Camera</gizmoSpace>");

            Assert.Equal(GizmoSpace.Camera, config.gizmoSpace);
            Assert.False(config.dirty);
        }

        [Theory]
        [InlineData("Camera")]
        [InlineData("Global")]
        public void 旧版でLocalへ戻した設定はLocalになる(string savedSpace)
        {
            // 旧版は gizmoSpace を知らず bool だけ書き換えるため、bool 側を正とする
            var config = Load(
                "<gizmoUseLocalSpace>true</gizmoUseLocalSpace><gizmoSpace>" + savedSpace + "</gizmoSpace>");

            Assert.Equal(GizmoSpace.Local, config.gizmoSpace);
            Assert.True(config.dirty);
        }

        [Fact]
        public void 旧版でGlobalへ切り替えた設定はGlobalになる()
        {
            var config = Load(
                "<gizmoUseLocalSpace>false</gizmoUseLocalSpace><gizmoSpace>Local</gizmoSpace>");

            Assert.Equal(GizmoSpace.Global, config.gizmoSpace);
            Assert.True(config.dirty);
        }
    }
}
