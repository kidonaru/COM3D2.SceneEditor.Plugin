using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneEditor の UI 倍率を外部プラグインと共有する公開 API。
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

        /// <summary>
        /// ゲストの設定画面から SceneEditor の UI 倍率を変える (後発。旧ホストには無い)。
        /// 値は設定画面と同じ刻み・範囲へ丸める。SceneEditor が無効なら書かずに false を返し、
        /// ゲストは自前の設定へ書く
        /// </summary>
        public static bool SetUIScale(float scale)
        {
            var config = ConfigManager.instance.config;
            if (!config.pluginEnabled || float.IsNaN(scale) || float.IsInfinity(scale))
            {
                return false;
            }

            var snapped = UIScaleSetting.Snap(scale);
            if (config.uiScale != snapped)
            {
                config.uiScale = snapped;
                config.dirty = true;
            }
            return true;
        }
    }
}
