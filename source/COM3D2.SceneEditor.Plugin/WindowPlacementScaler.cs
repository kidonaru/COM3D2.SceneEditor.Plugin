using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 画面サイズ変化に合わせてウィンドウ配置をスケールする純粋関数。
    /// 位置・サイズとも各軸独立の比率で調整し、画面に対する占有比率を維持する
    /// </summary>
    public static class WindowPlacementScaler
    {
        /// <summary>
        /// rect を基準画面サイズ→新画面サイズの比率でスケールする。
        /// 基準サイズが無効 (0 以下) または変化がない場合はそのまま返す
        /// </summary>
        public static Rect Scale(
            Rect rect,
            float baseScreenWidth, float baseScreenHeight,
            float newScreenWidth, float newScreenHeight,
            float minWidth, float minHeight)
        {
            if (baseScreenWidth <= 0f || baseScreenHeight <= 0f ||
                newScreenWidth <= 0f || newScreenHeight <= 0f)
            {
                return rect;
            }

            var ratioX = newScreenWidth / baseScreenWidth;
            var ratioY = newScreenHeight / baseScreenHeight;
            if (ratioX == 1f && ratioY == 1f)
            {
                return rect;
            }

            return new Rect(
                rect.x * ratioX,
                rect.y * ratioY,
                Mathf.Max(rect.width * ratioX, minWidth),
                Mathf.Max(rect.height * ratioY, minHeight));
        }

        /// <summary>
        /// 位置のみスケールする (サイズが内容から決まるメニューバー用)
        /// </summary>
        public static Vector2 ScalePosition(
            Vector2 position,
            float baseScreenWidth, float baseScreenHeight,
            float newScreenWidth, float newScreenHeight)
        {
            if (baseScreenWidth <= 0f || baseScreenHeight <= 0f ||
                newScreenWidth <= 0f || newScreenHeight <= 0f)
            {
                return position;
            }

            return new Vector2(
                position.x * (newScreenWidth / baseScreenWidth),
                position.y * (newScreenHeight / baseScreenHeight));
        }
    }
}
