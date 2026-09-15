using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ImageOutputLayoutTests
    {
        [Fact]
        public void 描画サイズ_画面が横長なら高さ基準で幅を広げる()
        {
            // 画面 21:9 (2.333)、出力 16:9 → 高さ 1080 を保ち幅を 2520 に伸ばす
            int w, h;
            ImageOutputLayout.GetRenderSize(2520f / 1080f, new Vector2(1920, 1080), out w, out h);
            Assert.Equal(2520, w);
            Assert.Equal(1080, h);
        }

        [Fact]
        public void 描画サイズ_画面が縦長なら幅基準で高さを伸ばす()
        {
            // 画面 4:3、出力 16:9 → 幅 1920 を保ち高さを 1440 に伸ばす
            int w, h;
            ImageOutputLayout.GetRenderSize(4f / 3f, new Vector2(1920, 1080), out w, out h);
            Assert.Equal(1920, w);
            Assert.Equal(1440, h);
        }

        [Fact]
        public void 描画サイズ_同じアスペクトなら出力サイズそのまま()
        {
            int w, h;
            ImageOutputLayout.GetRenderSize(16f / 9f, new Vector2(1280, 720), out w, out h);
            Assert.Equal(1280, w);
            Assert.Equal(720, h);
        }

        [Fact]
        public void 切り出し_横長の描画から中央を切り出す()
        {
            var rect = ImageOutputLayout.GetCropRect(2520, 1080, 1920, 1080);
            Assert.Equal(300f, rect.x, 3);
            Assert.Equal(0f, rect.y, 3);
            Assert.Equal(1920f, rect.width, 3);
            Assert.Equal(1080f, rect.height, 3);
        }

        [Fact]
        public void 切り出し_縦長の描画から中央を切り出す()
        {
            var rect = ImageOutputLayout.GetCropRect(1920, 1440, 1920, 1080);
            Assert.Equal(0f, rect.x, 3);
            Assert.Equal(180f, rect.y, 3);
            Assert.Equal(1920f, rect.width, 3);
            Assert.Equal(1080f, rect.height, 3);
        }

        [Fact]
        public void 出力サイズ_ゼロ以下は1に丸める()
        {
            int w, h;
            ImageOutputLayout.ClampImageSize(new Vector2(0, -5), out w, out h);
            Assert.Equal(1, w);
            Assert.Equal(1, h);
        }
    }
}
