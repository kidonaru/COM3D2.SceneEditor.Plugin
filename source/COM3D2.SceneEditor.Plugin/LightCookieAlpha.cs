using System;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スポットライトの輪郭 (cookie) のアルファ計算。
    /// ビルトイン RP のスポットは cookie のアルファだけを明るさに使い、
    /// cookie の形がそのまま光の形になるため、外周は必ず 0 にする (0 でないと光が四角く漏れる)。
    /// ゲーム外のテストから呼べるよう Mathf ではなく System.Math を使う
    /// </summary>
    public static class LightCookieAlpha
    {
        /// <summary>
        /// 円形のアルファを生成する。hardness は内側の明るい円の半径の比率で、
        /// 1 でも縁の 1px はぼかしてジャギを抑える。配列は下の行から並ぶ (Texture2D の生データと同じ)
        /// </summary>
        public static byte[] BuildRadial(int size, float hardness)
        {
            var alpha = new byte[size * size];
            var pixel = 2f / size;
            var outer = 1f - pixel;
            var clamped = Math.Max(0f, Math.Min(1f, hardness));
            var inner = Math.Min(clamped * outer, outer - pixel);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) * pixel - 1f;
                    var dy = (y + 0.5f) * pixel - 1f;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    var t = Math.Max(0f, Math.Min(1f, (r - inner) / (outer - inner)));
                    var a = 1f - t * t * (3f - 2f * t);
                    alpha[y * size + x] = (byte)Math.Round(a * 255f);
                }
            }

            ClearBorder(alpha, size, size);
            return alpha;
        }

        /// <summary>
        /// 画像の画素を cookie のアルファへ変換する。透過を持つ画像はアルファを、
        /// 持たない画像 (白黒の模様画像など) は輝度を使う
        /// </summary>
        public static byte[] FromPixels(Color32[] pixels, int width, int height)
        {
            var hasTransparency = false;
            foreach (var p in pixels)
            {
                if (p.a < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }

            var alpha = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                alpha[i] = hasTransparency
                    ? p.a
                    : (byte)Math.Round(0.299 * p.r + 0.587 * p.g + 0.114 * p.b);
            }

            ClearBorder(alpha, width, height);
            return alpha;
        }

        public static void ClearBorder(byte[] alpha, int width, int height)
        {
            for (var x = 0; x < width; x++)
            {
                alpha[x] = 0;
                alpha[(height - 1) * width + x] = 0;
            }
            for (var y = 0; y < height; y++)
            {
                alpha[y * width] = 0;
                alpha[y * width + width - 1] = 0;
            }
        }
    }
}
