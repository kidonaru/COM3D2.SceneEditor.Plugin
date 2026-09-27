using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>GameView の表示比率。Config に保存するため値の並びは変えないこと</summary>
    public enum GameViewAspectMode
    {
        Screen,
        Ratio16x9,
        Ratio4x3,
        Ratio1x1,
        Ratio3x4,
        Ratio9x16,
        Custom,
    }

    /// <summary>
    /// GameView の表示比率の計算。RT は画面解像度のまま描き、表示と撮影で中央を切り出す。
    /// 表示・マウス座標変換・画面分割グリッド・撮影がすべてここを通るので、
    /// 見た目と撮影結果の構図が食い違わない
    /// </summary>
    public static class GameViewAspect
    {
        /// <summary>カスタムサイズの上限。GPU の上限とは別に、入力ミスで巨大な撮影をしないための枠</summary>
        public const int MAX_CUSTOM_SIZE = 8192;

        /// <summary>コンボの並び順</summary>
        public static readonly GameViewAspectMode[] modes =
        {
            GameViewAspectMode.Screen,
            GameViewAspectMode.Ratio16x9,
            GameViewAspectMode.Ratio4x3,
            GameViewAspectMode.Ratio1x1,
            GameViewAspectMode.Ratio3x4,
            GameViewAspectMode.Ratio9x16,
            GameViewAspectMode.Custom,
        };

        public static string GetDisplayName(GameViewAspectMode mode)
        {
            switch (mode)
            {
                case GameViewAspectMode.Ratio16x9: return "16:9";
                case GameViewAspectMode.Ratio4x3: return "4:3";
                case GameViewAspectMode.Ratio1x1: return "1:1";
                case GameViewAspectMode.Ratio3x4: return "3:4";
                case GameViewAspectMode.Ratio9x16: return "9:16";
                case GameViewAspectMode.Custom: return "カスタム";
                default: return "画面";
            }
        }

        /// <summary>切り出す比率 (幅 / 高さ)。画面全体 (切り出しなし) なら false</summary>
        public static bool TryGetAspect(
            GameViewAspectMode mode, int customWidth, int customHeight, out float aspect)
        {
            switch (mode)
            {
                case GameViewAspectMode.Ratio16x9: aspect = 16f / 9f; return true;
                case GameViewAspectMode.Ratio4x3: aspect = 4f / 3f; return true;
                case GameViewAspectMode.Ratio1x1: aspect = 1f; return true;
                case GameViewAspectMode.Ratio3x4: aspect = 3f / 4f; return true;
                case GameViewAspectMode.Ratio9x16: aspect = 9f / 16f; return true;
                case GameViewAspectMode.Custom:
                    aspect = (float)Mathf.Max(customWidth, 1) / Mathf.Max(customHeight, 1);
                    return true;
                default:
                    aspect = 0f;
                    return false;
            }
        }

        /// <summary>画面から targetAspect を中央で切り出す範囲 (0〜1)。横長なら上下を、縦長なら左右を切る</summary>
        public static Rect GetCropUV(float screenAspect, float targetAspect)
        {
            if (targetAspect >= screenAspect)
            {
                var height = screenAspect / targetAspect;
                return new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }

            var width = targetAspect / screenAspect;
            return new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        /// <summary>
        /// 撮影の描画サイズと切り出し矩形。出力サイズは cropRect のサイズ。
        /// カスタムは指定サイズちょうどを出すため倍率を使わず、画面アスペクトで描いて中央を切り出す
        /// (連番出力と同じ ImageOutputLayout の方式)。
        /// 描画サイズが GPU の上限を超えると RenderTexture を確保できないため、描画・出力を同率で縮める
        /// </summary>
        public static void GetCaptureLayout(
            int screenWidth, int screenHeight, int scale,
            GameViewAspectMode mode, int customWidth, int customHeight, int maxTextureSize,
            out int renderWidth, out int renderHeight, out Rect cropRect)
        {
            screenWidth = Mathf.Max(screenWidth, 1);
            screenHeight = Mathf.Max(screenHeight, 1);
            var screenAspect = (float)screenWidth / screenHeight;

            int outputWidth, outputHeight;
            float aspect;
            if (mode == GameViewAspectMode.Custom)
            {
                outputWidth = Mathf.Clamp(customWidth, 1, MAX_CUSTOM_SIZE);
                outputHeight = Mathf.Clamp(customHeight, 1, MAX_CUSTOM_SIZE);
                ImageOutputLayout.GetRenderSize(screenAspect, new Vector2(outputWidth, outputHeight),
                    out renderWidth, out renderHeight);
            }
            else
            {
                renderWidth = screenWidth * scale;
                renderHeight = screenHeight * scale;
                outputWidth = renderWidth;
                outputHeight = renderHeight;
                if (TryGetAspect(mode, customWidth, customHeight, out aspect))
                {
                    var uv = GetCropUV(screenAspect, aspect);
                    outputWidth = Mathf.Max(Mathf.RoundToInt(renderWidth * uv.width), 1);
                    outputHeight = Mathf.Max(Mathf.RoundToInt(renderHeight * uv.height), 1);
                }
            }

            // 縦横を個別にクランプすると絵が歪むので、はみ出した分の比率を全辺へ等しくかける
            var longest = Mathf.Max(renderWidth, renderHeight);
            if (longest > maxTextureSize)
            {
                var ratio = (float)maxTextureSize / longest;
                renderWidth = Mathf.Max(Mathf.RoundToInt(renderWidth * ratio), 1);
                renderHeight = Mathf.Max(Mathf.RoundToInt(renderHeight * ratio), 1);
                outputWidth = Mathf.Clamp(Mathf.RoundToInt(outputWidth * ratio), 1, renderWidth);
                outputHeight = Mathf.Clamp(Mathf.RoundToInt(outputHeight * ratio), 1, renderHeight);
            }

            cropRect = ImageOutputLayout.GetCropRect(renderWidth, renderHeight, outputWidth, outputHeight);
        }
    }
}
