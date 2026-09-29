using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneEditor の UI 倍率を外部プラグインへ伝える公開 API。
    /// MTEUtils の UIScaleClient からリフレクションで発見されるため、
    /// クラス名・シグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約: SceneEditor が有効 (pluginEnabled) なら倍率 (GUIScale の許容範囲内)、無効なら 0 を返す。
    /// 0 を受けたゲストは自前の設定を使う
    /// </summary>
    public static class UIScaleHost
    {
        public static float uiScale
        {
            get
            {
                var config = ConfigManager.instance.config;
                return config.pluginEnabled ? GUIScale.ClampScale(config.uiScale) : 0f;
            }
        }
    }
}
