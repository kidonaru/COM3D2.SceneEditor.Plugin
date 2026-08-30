using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細のパラメータをウィンドウ幅に合わせて折り返すためのレイアウト計算。
    /// GUI に依存しない純粋計算なので単体テストできる
    /// </summary>
    public static class KeyFrameFlowLayout
    {
        /// <summary>
        /// 指定幅に何列並べられるかを返す。
        /// 1 列目は itemWidth、2 列目以降は margin + itemWidth を消費する。
        /// 1 列も入らない場合でも 1 を返す (幅を切り詰めてでも 1 列は出す)
        /// </summary>
        public static int GetColumnCount(float availableWidth, float itemWidth, float margin)
        {
            if (itemWidth <= 0f || availableWidth < itemWidth)
            {
                return 1;
            }

            var count = 1 + Mathf.FloorToInt((availableWidth - itemWidth) / (itemWidth + margin));
            return Mathf.Max(1, count);
        }
    }
}
