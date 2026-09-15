using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class VideoPreviewPlacementTests
    {
        [Fact]
        public void 未設定の添字でも既定値の配置が返る()
        {
            var config = new Config();

            var placement = config.GetVideoPreview(2);

            Assert.Equal(-1, placement.posX);
            Assert.Equal(-1, placement.posY);
            Assert.Equal(480, placement.width);
            Assert.Equal(270, placement.height);
            Assert.False(placement.visible);
        }

        [Fact]
        public void 添字ごとに別の配置が保持される()
        {
            var config = new Config();

            config.GetVideoPreview(0).posX = 100;
            config.GetVideoPreview(1).posX = 200;

            Assert.Equal(100, config.GetVideoPreview(0).posX);
            Assert.Equal(200, config.GetVideoPreview(1).posX);
        }

        [Fact]
        public void 範囲外の添字は端の配置に丸められる()
        {
            var config = new Config();

            config.GetVideoPreview(0).posX = 100;
            config.GetVideoPreview(3).posX = 400;

            Assert.Equal(100, config.GetVideoPreview(-1).posX);
            Assert.Equal(400, config.GetVideoPreview(99).posX);
        }
    }
}
