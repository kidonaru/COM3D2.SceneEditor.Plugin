using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class VideoPreviewViewMathTests
    {
        private static readonly Rect Area = new Rect(10f, 20f, 200f, 100f);
        private static readonly Vector2 BaseSize = new Vector2(300f, 100f);

        [Fact]
        public void 拡大と縮小で1段ずつ倍率が変わる()
        {
            Assert.Equal(1.25f, VideoPreviewViewMath.StepZoom(1f, 1), 4);
            Assert.Equal(0.8f, VideoPreviewViewMath.StepZoom(1f, -1), 4);
        }

        [Fact]
        public void 倍率は上下限でクランプする()
        {
            Assert.Equal(VideoPreviewViewMath.MaxZoom, VideoPreviewViewMath.StepZoom(VideoPreviewViewMath.MaxZoom, 1), 4);
            Assert.Equal(VideoPreviewViewMath.MinZoom, VideoPreviewViewMath.StepZoom(VideoPreviewViewMath.MinZoom, -1), 4);
        }

        [Fact]
        public void 等倍で位置ずらし無しなら領域の中央に置く()
        {
            // 領域 (10, 20, 200x100) の中央に 300x100 を置くと左右に 50 ずつはみ出す
            var rect = VideoPreviewViewMath.ApplyView(BaseSize, Area, 1f, Vector2.zero);

            Assert.Equal(-40f, rect.x, 3);
            Assert.Equal(20f, rect.y, 3);
            Assert.Equal(300f, rect.width, 3);
            Assert.Equal(100f, rect.height, 3);
        }

        [Fact]
        public void 倍率は領域の中心を基準に掛かり位置ずらしは領域サイズ比で動く()
        {
            // 領域中心 (110, 70)。2 倍で 600x200、位置ずらし (0.25, -0.5) で中心は (160, 20)
            var rect = VideoPreviewViewMath.ApplyView(BaseSize, Area, 2f, new Vector2(0.25f, -0.5f));

            Assert.Equal(600f, rect.width, 3);
            Assert.Equal(200f, rect.height, 3);
            Assert.Equal(160f, rect.center.x, 3);
            Assert.Equal(20f, rect.center.y, 3);
        }

        [Fact]
        public void カーソル下の点を固定して倍率を変える()
        {
            var cursor = new Vector2(140f, 80f);
            var pan = new Vector2(0.1f, 0.2f);

            var before = VideoPreviewViewMath.ApplyView(BaseSize, Area, 1.5f, pan);
            var newPan = VideoPreviewViewMath.ZoomAt(pan, Area, BaseSize, cursor, 1.5f, 3f);
            var after = VideoPreviewViewMath.ApplyView(BaseSize, Area, 3f, newPan);

            // 動画上の相対位置 (0〜1) がズーム前後で変わらない
            Assert.Equal((cursor.x - before.x) / before.width, (cursor.x - after.x) / after.width, 4);
            Assert.Equal((cursor.y - before.y) / before.height, (cursor.y - after.y) / after.height, 4);
        }

        [Fact]
        public void 高倍率でも動画の隅を中心にズームできる()
        {
            // 4 倍 (1200x400) で右下の隅寄りを表示。8 倍でも上限 (6, 4) に掛からず点が固定される
            var cursor = new Vector2(200f, 110f);
            var pan = new Vector2(2.5f, 1.5f);

            var before = VideoPreviewViewMath.ApplyView(BaseSize, Area, 4f, pan);
            var newPan = VideoPreviewViewMath.ZoomAt(pan, Area, BaseSize, cursor, 4f, 8f);
            var after = VideoPreviewViewMath.ApplyView(BaseSize, Area, 8f, newPan);

            Assert.Equal((cursor.x - before.x) / before.width, (cursor.x - after.x) / after.width, 4);
            Assert.Equal((cursor.y - before.y) / before.height, (cursor.y - after.y) / after.height, 4);
        }

        [Fact]
        public void ドラッグ開始時の位置ずらしに移動量を領域サイズ比で足す()
        {
            var pan = VideoPreviewViewMath.DragPan(
                new Vector2(0.1f, 0f), Area, BaseSize, new Vector2(20f, -10f));

            Assert.Equal(0.2f, pan.x, 4);
            Assert.Equal(-0.1f, pan.y, 4);
        }

        [Fact]
        public void 拡大中は動画の端が領域の中心まで来られる()
        {
            // 描画 1200x400 の半分を領域 200x100 で割った (3, 2) が上限
            var pan = VideoPreviewViewMath.ClampPan(new Vector2(10f, -10f), Area, new Vector2(1200f, 400f));

            Assert.Equal(3f, pan.x, 4);
            Assert.Equal(-2f, pan.y, 4);
        }

        [Fact]
        public void 動画が領域より小さくても中心は領域の外へ出ない()
        {
            var pan = VideoPreviewViewMath.ClampPan(new Vector2(0.8f, -0.7f), Area, new Vector2(100f, 50f));

            Assert.Equal(0.5f, pan.x, 4);
            Assert.Equal(-0.5f, pan.y, 4);
        }

        [Fact]
        public void 領域が潰れている軸の移動量は無視する()
        {
            var pan = VideoPreviewViewMath.DragPan(
                Vector2.zero, new Rect(0f, 0f, 0f, 100f), BaseSize, new Vector2(20f, 10f));

            Assert.Equal(0f, pan.x, 4);
            Assert.Equal(0.1f, pan.y, 4);
        }

        [Fact]
        public void 倍率の表示は百分率の整数()
        {
            Assert.Equal("100%", VideoPreviewViewMath.FormatZoom(1f));
            Assert.Equal("156%", VideoPreviewViewMath.FormatZoom(1.5625f));
        }
    }
}
