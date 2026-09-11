using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 連番画像出力の描画サイズと切り出し矩形の計算。
    /// 画面と同じアスペクトで描いてから中央を切り出すことで、
    /// 出力サイズが画面と違うアスペクトでも画面上の構図とレターボックスを保つ
    /// </summary>
    public static class ImageOutputLayout
    {
        // COM3D2 2.0 側の Unity には Vector2Int が無いため out 引数で返す

        /// <summary>出力サイズを 1px 以上の整数に丸める</summary>
        public static void ClampImageSize(Vector2 imageSize, out int width, out int height)
        {
            width = Mathf.Max(Mathf.RoundToInt(imageSize.x), 1);
            height = Mathf.Max(Mathf.RoundToInt(imageSize.y), 1);
        }

        /// <summary>
        /// 画面アスペクトを保ちつつ出力サイズを覆う描画サイズ。
        /// 画面が出力より横長なら高さを、縦長なら幅を基準にもう一方を伸ばす
        /// </summary>
        public static void GetRenderSize(float screenAspect, Vector2 imageSize, out int width, out int height)
        {
            int imageWidth, imageHeight;
            ClampImageSize(imageSize, out imageWidth, out imageHeight);
            var imageAspect = (float)imageWidth / imageHeight;
            // 丸め誤差で出力サイズを下回ると切り出し矩形が負になるため、出力サイズを下限にする
            if (screenAspect > imageAspect)
            {
                width = Mathf.Max(Mathf.RoundToInt(imageHeight * screenAspect), imageWidth);
                height = imageHeight;
            }
            else
            {
                width = imageWidth;
                height = Mathf.Max(Mathf.RoundToInt(imageWidth / screenAspect), imageHeight);
            }
        }

        /// <summary>描画結果から出力サイズ分を中央で切り出す矩形 (ReadPixels 用)</summary>
        public static Rect GetCropRect(int renderWidth, int renderHeight, int outputWidth, int outputHeight)
        {
            var x = (renderWidth - outputWidth) * 0.5f;
            var y = (renderHeight - outputHeight) * 0.5f;
            return new Rect(x, y, outputWidth, outputHeight);
        }
    }
}
