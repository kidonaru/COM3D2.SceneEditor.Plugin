using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>GameView の表示比率の切り出しと撮影サイズを固定する</summary>
    public class GameViewAspectTests
    {
        private const int NoLimit = 100000;

        [Fact]
        public void 比率_画面は切り出しなし()
        {
            float aspect;
            Assert.False(GameViewAspect.TryGetAspect(GameViewAspectMode.Screen, 1080, 1920, out aspect));
        }

        [Fact]
        public void 比率_カスタムは幅と高さの比()
        {
            float aspect;
            Assert.True(GameViewAspect.TryGetAspect(GameViewAspectMode.Custom, 1080, 1920, out aspect));
            Assert.Equal(1080f / 1920f, aspect, 5);
        }

        [Fact]
        public void 比率_カスタムの0以下は1として扱う()
        {
            float aspect;
            Assert.True(GameViewAspect.TryGetAspect(GameViewAspectMode.Custom, 0, -5, out aspect));
            Assert.Equal(1f, aspect, 5);
        }

        [Fact]
        public void 切り出し_縦長の比率は左右を切る()
        {
            // 16:9 の画面から 9:16 → 幅 (9/16)/(16/9) = 0.3164 を中央に
            var uv = GameViewAspect.GetCropUV(16f / 9f, 9f / 16f);
            Assert.Equal(0.31640625f, uv.width, 5);
            Assert.Equal(1f, uv.height, 5);
            Assert.Equal((1f - 0.31640625f) * 0.5f, uv.x, 5);
            Assert.Equal(0f, uv.y, 5);
        }

        [Fact]
        public void 切り出し_横長の比率は上下を切る()
        {
            // 4:3 の画面から 16:9 → 高さ (4/3)/(16/9) = 0.75
            var uv = GameViewAspect.GetCropUV(4f / 3f, 16f / 9f);
            Assert.Equal(1f, uv.width, 5);
            Assert.Equal(0.75f, uv.height, 5);
            Assert.Equal(0.125f, uv.y, 5);
        }

        [Fact]
        public void 撮影_画面は画面サイズ掛ける倍率()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 2, GameViewAspectMode.Screen, 0, 0, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(3840, rw);
            Assert.Equal(2160, rh);
            Assert.Equal(new Rect(0, 0, 3840, 2160), crop);
        }

        [Fact]
        public void 撮影_比率は切り出し範囲掛ける倍率()
        {
            // 1920x1080 の 3:4 → 810x1080、2 倍で 1620x2160
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 2, GameViewAspectMode.Ratio3x4, 0, 0, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(3840, rw);
            Assert.Equal(2160, rh);
            Assert.Equal(1620f, crop.width);
            Assert.Equal(2160f, crop.height);
            Assert.Equal((3840f - 1620f) * 0.5f, crop.x);
        }

        [Fact]
        public void 撮影_カスタムは指定サイズちょうどで倍率を使わない()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 4, GameViewAspectMode.Custom, 1080, 1920, NoLimit,
                out rw, out rh, out crop);
            Assert.Equal(1080f, crop.width);
            Assert.Equal(1920f, crop.height);
            // 画面アスペクト (16:9) で高さ 1920 を覆う → 幅 3413
            Assert.Equal(3413, rw);
            Assert.Equal(1920, rh);
        }

        [Fact]
        public void 撮影_カスタムの切り出しは表示のUVと同じ範囲()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 1, GameViewAspectMode.Custom, 1080, 1920, NoLimit,
                out rw, out rh, out crop);
            var uv = GameViewAspect.GetCropUV(1920f / 1080f, 1080f / 1920f);
            Assert.Equal(uv.x, crop.x / rw, 2);
            Assert.Equal(uv.width, crop.width / rw, 2);
        }

        [Fact]
        public void 撮影_GPU上限を超えたら描画と出力を同率で縮める()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(3840, 2160, 4, GameViewAspectMode.Ratio9x16, 0, 0, 8192,
                out rw, out rh, out crop);
            Assert.True(Mathf.Max(rw, rh) <= 8192);
            Assert.True(crop.width <= rw && crop.height <= rh);
            Assert.True(crop.x >= 0f && crop.y >= 0f);
            // 9:16 のまま
            Assert.Equal(9f / 16f, crop.width / crop.height, 2);
        }

        [Fact]
        public void 撮影_カスタムが上限を超えても例外にならない()
        {
            int rw, rh;
            Rect crop;
            GameViewAspect.GetCaptureLayout(1920, 1080, 1, GameViewAspectMode.Custom, 8192, 8192, 8192,
                out rw, out rh, out crop);
            Assert.True(Mathf.Max(rw, rh) <= 8192);
            Assert.Equal(crop.width, crop.height, 0);
        }
    }
}
