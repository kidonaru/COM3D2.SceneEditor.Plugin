using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// EditorSubWindow / GameView の矩形計算 (UI 倍率の影響を受ける部分) を純関数にまとめる。
    /// EditorSubWindow の headerHeight はツールバー用の上余白 (contentTopMargin) を含めた値を渡す
    /// </summary>
    public static class EditorWindowGeometry
    {
        /// <summary>
        /// 内容の描画領域 (実矩形)。ヘッダーと枠は窓ごと拡大されるため厚みに倍率を掛ける。
        /// 縁は整数ピクセルへ丸める (端数の位置へ RT を描くとにじみ、ピック座標もずれるため)
        /// </summary>
        public static Rect GetContentRect(Rect windowRect, float headerHeight, float frame, float scale)
        {
            var xMin = Mathf.Round(windowRect.x + frame * scale);
            var yMin = Mathf.Round(windowRect.y + headerHeight * scale);
            var xMax = Mathf.Round(windowRect.xMax - frame * scale);
            var yMax = Mathf.Round(windowRect.yMax - frame * scale);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// config・レイアウトへ保存する内容サイズ。倍率を掛けない従来の式にして、
        /// 倍率を変えても復元後の窓の実サイズが変わらないようにする (保存値の意味も従来のまま)
        /// </summary>
        public static Vector2 GetPlacementContentSize(Rect windowRect, float headerHeight, float frame)
        {
            return new Vector2(windowRect.width - frame * 2f, windowRect.height - headerHeight - frame);
        }

        /// <summary>
        /// 配置の矩形を整数ピクセルへそろえる。画面サイズ変更のスケーリングで端数が残ると、
        /// 内容領域の縁を別々に丸めるため、ドラッグで位置が整数へ丸まったときに幅が ±1px 変わり RT とずれる
        /// </summary>
        public static Rect RoundRect(Rect rect)
        {
            return new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.width), Mathf.Round(rect.height));
        }

        /// <summary>配置保存値から窓の実サイズを求める (GetPlacementContentSize の逆)。最小サイズは呼び出し側で掛ける</summary>
        public static Vector2 GetWindowSize(Vector2 placementContentSize, float headerHeight, float frame)
        {
            return new Vector2(
                placementContentSize.x + frame * 2f, placementContentSize.y + headerHeight + frame);
        }
    }
}
