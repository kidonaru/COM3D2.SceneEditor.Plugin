namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>履歴エントリが対象とする状態の範囲</summary>
    public enum HistoryScope
    {
        /// <summary>ポーズ (操作対象ボーンの Transform・目線・モーション再生状態)</summary>
        Pose,
        /// <summary>表情 (モーフ値・まばたき)</summary>
        Face,
        /// <summary>脱衣 (スロットマスク・めくれ系)</summary>
        Undress,
        /// <summary>一般オブジェクトの Transform と表示状態 (メイドルート・ライト等)</summary>
        Object,
        /// <summary>IK 固定フラグと接地パラメータ</summary>
        IK,
        /// <summary>背景 (種類・位置・回転・背景色)</summary>
        Background,
        /// <summary>ライト (メイン・追加ライト一覧)</summary>
        Light,
        /// <summary>メインカメラの構図</summary>
        Camera,
        /// <summary>呼出済みメイドの配置</summary>
        Placement,
        /// <summary>重力 (髪・スカートの有効フラグとオフセット)</summary>
        Gravity,
        /// <summary>PNG 配置 (配置一覧・色・表示順)</summary>
        PngPlacement,
        /// <summary>マテリアル 1 件 (色・数値プロパティと追跡チェック)。メイド・モデル・背景で共用</summary>
        Material,
        /// <summary>シェイプキー 1 件 (重みと追跡チェック)。メイド・モデルで共用</summary>
        ShapeKey,
        /// <summary>フリーテキスト全件 (件数含む)</summary>
        Text,
        /// <summary>サブカメラ全台 (台数含む)</summary>
        SubCamera,
        /// <summary>BGM ファイル設定 (パス・BPM・BPM ライン)</summary>
        Sound,
        /// <summary>動画全本 (本数含む)</summary>
        Video,
        /// <summary>ライブ演出 (ステージライト・レーザー・サイリウムの全体状態)</summary>
        LiveEffect,
    }

    public static class HistoryScopeUtils
    {
        /// <summary>
        /// メイドに状態を書き戻せるか。呼出解除や着替え中は書き戻しても失われるため false。
        /// メイドに紐付くスコープの CanApply はすべてこの判定を共有する
        /// </summary>
        public static bool CanEditMaid(Maid maid)
        {
            return maid != null && maid.body0 != null && !maid.IsAllProcPropBusy;
        }

        /// <summary>
        /// 対象メイドが必須のスコープか。
        /// 環境系と配置は特定のメイドに紐付かないため maid なしで記録する
        /// </summary>
        public static bool RequiresMaid(HistoryScope scope)
        {
            switch (scope)
            {
                case HistoryScope.Object:
                case HistoryScope.Background:
                case HistoryScope.Light:
                case HistoryScope.Camera:
                case HistoryScope.Placement:
                case HistoryScope.PngPlacement:
                // 対象はスナップショット側が保持する。メイドは自動キーフレーム登録の判定用に任意で付く
                case HistoryScope.Material:
                case HistoryScope.ShapeKey:
                case HistoryScope.Text:
                case HistoryScope.SubCamera:
                case HistoryScope.Sound:
                case HistoryScope.Video:
                case HistoryScope.LiveEffect:
                    return false;
                default:
                    return true;
            }
        }
    }
}
