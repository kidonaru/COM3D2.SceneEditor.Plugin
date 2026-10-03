using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 動画プレビューの表示ズーム・位置ずらしの計算 (純粋ロジック、単体テスト対象)。
    /// 位置ずらし (pan) は表示領域の中心から動画の中心までの距離を領域サイズ比で持つ。
    /// ウィンドウをリサイズしても見ている場所が変わらないようにするため
    /// </summary>
    public static class VideoPreviewViewMath
    {
        /// <summary>
        /// 縮小側の下限。等倍は領域を覆う表示で端が切れるため、1 未満まで下げて全体を見られるようにする
        /// </summary>
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 16f;

        /// <summary>ボタン・ホイール 1 段あたりの倍率</summary>
        public const float ZoomStep = 1.25f;

        /// <summary>
        /// 動画の中心を置ける範囲の最小値 (領域サイズ比、中心から片側)。
        /// 動画が領域より小さくても、中心は領域内に留めて見失わないようにする
        /// </summary>
        private const float MinPanLimit = 0.5f;

        /// <summary>1 段拡大 (direction &gt; 0) / 縮小 (direction &lt; 0) した倍率</summary>
        public static float StepZoom(float zoom, int direction)
        {
            return Mathf.Clamp(zoom * Mathf.Pow(ZoomStep, direction), MinZoom, MaxZoom);
        }

        /// <summary>
        /// 等倍時の動画サイズ baseSize に表示ズームと位置ずらしを掛けた描画矩形。
        /// 等倍・位置ずらし無しで領域の中央に置き、倍率は領域の中心を基準に掛ける
        /// </summary>
        public static Rect ApplyView(Vector2 baseSize, Rect area, float zoom, Vector2 pan)
        {
            var size = baseSize * zoom;
            var center = area.center + Vector2.Scale(pan, area.size);
            return new Rect(center - size * 0.5f, size);
        }

        /// <summary>
        /// 倍率を oldZoom から newZoom へ変えたあとも、cursor の下の点が動かない位置ずらし。
        /// 範囲の端でクランプされたときは、その分だけカーソル下の点がずれる
        /// </summary>
        public static Vector2 ZoomAt(
            Vector2 pan, Rect area, Vector2 baseSize, Vector2 cursor, float oldZoom, float newZoom)
        {
            var oldCenter = area.center + Vector2.Scale(pan, area.size);
            var newCenter = cursor - (cursor - oldCenter) * (newZoom / oldZoom);
            return ClampPan(DivideBySize(newCenter - area.center, area), area, baseSize * newZoom);
        }

        /// <summary>
        /// ドラッグ開始時の位置ずらしに、開始点からのカーソル移動量 (px) を足す。
        /// 毎イベントの移動量を積むと UI 倍率下の座標系の違いや誤差が溜まるため、開始点からの差で求める
        /// </summary>
        public static Vector2 DragPan(Vector2 startPan, Rect area, Vector2 drawSize, Vector2 dragDistance)
        {
            return ClampPan(startPan + DivideBySize(dragDistance, area), area, drawSize);
        }

        /// <summary>
        /// 位置ずらしを、動画の端が領域の中心まで来られる範囲へ収める。
        /// 拡大中も動画の隅まで見られ、かつ動画が領域の外へ逃げ切らない
        /// </summary>
        public static Vector2 ClampPan(Vector2 pan, Rect area, Vector2 drawSize)
        {
            var limit = DivideBySize(drawSize * 0.5f, area);
            var limitX = Mathf.Max(limit.x, MinPanLimit);
            var limitY = Mathf.Max(limit.y, MinPanLimit);
            return new Vector2(
                Mathf.Clamp(pan.x, -limitX, limitX),
                Mathf.Clamp(pan.y, -limitY, limitY));
        }

        /// <summary>ツールバーに出す倍率 (百分率の整数)</summary>
        public static string FormatZoom(float zoom)
        {
            return Mathf.RoundToInt(zoom * 100f) + "%";
        }

        // 領域が潰れている軸は 0 を返す (0 除算で NaN が残ると描画が消えるため)
        private static Vector2 DivideBySize(Vector2 value, Rect area)
        {
            return new Vector2(
                area.width > 0f ? value.x / area.width : 0f,
                area.height > 0f ? value.y / area.height : 0f);
        }
    }
}
