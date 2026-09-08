namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>動画 1 本へ設定を書き戻したあとに必要なプレイヤー操作</summary>
    public enum VideoApplyAction
    {
        /// <summary>位置・色・音量・シークの反映だけ</summary>
        Update,
        /// <summary>表示切替 (読込は試みる) + 反映</summary>
        Visible,
        /// <summary>プレイヤーを作り直す</summary>
        Reload,
    }

    /// <summary>
    /// 動画の履歴適用で ReloadMovie を避けるための判定。
    /// プレイヤーの作り直しはパスと表示形式が変わったときだけ必要 (MovieManager.SetupImpl の条件)
    /// </summary>
    public static class VideoReloadPolicy
    {
        public static VideoApplyAction Decide(ScenePresetVideo before, ScenePresetVideo after)
        {
            if (before == null || after == null)
            {
                return VideoApplyAction.Reload;
            }
            if (before.path != after.path || before.displayType != after.displayType)
            {
                return VideoApplyAction.Reload;
            }
            if (before.enabled != after.enabled)
            {
                return VideoApplyAction.Visible;
            }
            return VideoApplyAction.Update;
        }
    }
}
