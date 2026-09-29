using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>タブのドラッグ並び替えの判定幅</summary>
    public static class TabReorderMath
    {
        /// <summary>
        /// タブ 1 枚ぶん並びを動かすのに要る画面上の移動量。タブ列は論理幅 (実幅 ÷ 倍率) で
        /// レイアウトされ、画面上ではその倍率倍に見えるため、描画側と同じ計算に倍率を掛ける。
        /// headerWidth は画面上の実幅
        /// </summary>
        public static float GetStep(float headerWidth, int tabCount, float guiScale)
        {
            // スクロール位置は幅の算出に関与しないため 0 を渡す
            var available = TabBarLayout.CalcAvailableWidth(headerWidth / guiScale);
            var layout = TabBarLayout.Calc(tabCount, available, 0f);
            // TabBarLayout.Calc は tabWidth >= MIN_TAB_WIDTH を保証するので step は必ず正
            // (0 だと呼び出し側の while が無限ループになる)
            return (layout.tabWidth + TabBarDrawer.TAB_MARGIN) * guiScale;
        }
    }
}
