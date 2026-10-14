using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
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

        private const int NoLimit = 100000;

        [Fact]
        public void レイアウト_カスタムは倍率を無視して指定サイズちょうど()
        {
            // 従来の出力 (21:9 画面で 1920x1080) と同じ描画・切り出しになること
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(2520, 1080, 3, GameViewAspectMode.Custom,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(2520, w);
            Assert.Equal(1080, h);
            Assert.Equal(300f, crop.x, 3);
            Assert.Equal(1920f, crop.width, 3);
            Assert.Equal(1080f, crop.height, 3);
        }

        [Fact]
        public void レイアウト_画面は画面サイズに倍率をかける()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 2, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(3840, w);
            Assert.Equal(2160, h);
            Assert.Equal(3840f, crop.width, 3);
            Assert.Equal(2160f, crop.height, 3);
        }

        [Fact]
        public void レイアウト_比率は倍率をかけた描画から中央を切り出す()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 2, GameViewAspectMode.Ratio1x1,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(3840, w);
            Assert.Equal(2160, h);
            Assert.Equal(2160f, crop.width, 3);
            Assert.Equal(2160f, crop.height, 3);
            Assert.Equal(840f, crop.x, 3);
        }

        [Theory]
        [InlineData(0, 1920)]
        [InlineData(-3, 1920)]
        [InlineData(9, 7680)]
        public void レイアウト_倍率は1から4にクランプする(int scale, int expectedWidth)
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, scale, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), NoLimit, out w, out h, out crop);
            Assert.Equal(expectedWidth, w);
        }

        [Fact]
        public void レイアウト_GPU上限を超える倍率は上限に収める()
        {
            // 4K ×4 = 15360x8640 を上限 8192 へ縦横比を保って縮める
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(3840, 2160, 4, GameViewAspectMode.Screen,
                new Vector2(1920, 1080), 8192, out w, out h, out crop);
            Assert.Equal(8192, w);
            Assert.Equal(4608, h);
            Assert.True(crop.width <= w && crop.height <= h);
        }

        [Fact]
        public void レイアウト_カスタムサイズの0以下は1に丸める()
        {
            int w, h;
            Rect crop;
            ImageOutputLayout.GetLayout(1920, 1080, 1, GameViewAspectMode.Custom,
                new Vector2(0, -10), NoLimit, out w, out h, out crop);
            Assert.Equal(1f, crop.width, 3);
            Assert.Equal(1f, crop.height, 3);
        }
    }
}
