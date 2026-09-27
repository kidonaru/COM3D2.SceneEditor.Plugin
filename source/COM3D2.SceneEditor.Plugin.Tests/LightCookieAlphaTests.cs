using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>スポットライトの輪郭 (cookie) のアルファ計算を固定する</summary>
    public class LightCookieAlphaTests
    {
        private const int Size = 64;

        private static byte At(byte[] alpha, int x, int y) => alpha[y * Size + x];

        [Fact]
        public void 生成した円は中心が不透明で外周と四隅は0()
        {
            var alpha = LightCookieAlpha.BuildRadial(Size, 0.8f);

            Assert.Equal(Size * Size, alpha.Length);
            Assert.Equal(255, At(alpha, Size / 2, Size / 2));
            for (var i = 0; i < Size; i++)
            {
                Assert.Equal(0, At(alpha, i, 0));
                Assert.Equal(0, At(alpha, i, Size - 1));
                Assert.Equal(0, At(alpha, 0, i));
                Assert.Equal(0, At(alpha, Size - 1, i));
            }
        }

        [Fact]
        public void 硬いほど縁の手前まで明るい()
        {
            // 半径 0.75 付近 (中心から右へ 3/8 * Size) の明るさを比べる
            var x = Size / 2 + Size * 3 / 8;
            var soft = At(LightCookieAlpha.BuildRadial(Size, 0f), x, Size / 2);
            var hard = At(LightCookieAlpha.BuildRadial(Size, 1f), x, Size / 2);

            Assert.True(hard > soft, $"hard={hard} soft={soft}");
            Assert.Equal(255, hard);
        }

        [Fact]
        public void 硬さは0から1へ丸める()
        {
            Assert.Equal(LightCookieAlpha.BuildRadial(Size, 1f), LightCookieAlpha.BuildRadial(Size, 5f));
            Assert.Equal(LightCookieAlpha.BuildRadial(Size, 0f), LightCookieAlpha.BuildRadial(Size, -1f));
        }

        [Fact]
        public void 透過の無い画像は輝度をアルファにする()
        {
            var pixels = Fill(3, 3, new Color32(255, 255, 255, 255));
            pixels[4] = new Color32(128, 128, 128, 255);

            var alpha = LightCookieAlpha.FromPixels(pixels, 3, 3);

            // 中央以外は外周なので 0
            Assert.Equal(new byte[] { 0, 0, 0, 0, 128, 0, 0, 0, 0 }, alpha);
        }

        [Fact]
        public void 透過のある画像はアルファをそのまま使う()
        {
            var pixels = Fill(3, 3, new Color32(0, 0, 0, 0));
            pixels[4] = new Color32(0, 0, 0, 200);

            var alpha = LightCookieAlpha.FromPixels(pixels, 3, 3);

            Assert.Equal(200, alpha[4]);
        }

        [Fact]
        public void 外周1pxを0にする()
        {
            var alpha = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }; // 4x3

            LightCookieAlpha.ClearBorder(alpha, 4, 3);

            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 9, 9, 0, 0, 0, 0, 0 }, alpha);
        }

        private static Color32[] Fill(int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            return pixels;
        }
    }
}
