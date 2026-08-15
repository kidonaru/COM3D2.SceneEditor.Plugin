using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タブドッキングの対象になれるウィンドウの抽象。
    /// EditorSubWindow のほか、外部プラグインのウィンドウを包む
    /// ExternalWindowAdapter (DockingHost.cs) も実装する。
    /// TabGroup / TabGroupManager はこの抽象だけに依存する
    /// </summary>
    public interface IDockableWindow : IConnectableWindow
    {
        /// <summary>
        /// 所属タブグループ。IConnectableWindow の get のみ定義を new で隠蔽して set を追加する。
        /// 実装側は単一の TabGroup group { get; set; } プロパティで両インターフェースを満たせる
        /// (IConnectableWindow を get;set; に変えると GameViewWindow 等の実装へ波及するため隠蔽方式を採る)
        /// </summary>
        new TabGroup group { get; set; }

        /// <summary>タブ表示名</summary>
        string windowTitleForTab { get; }

        /// <summary>スクリーンGUI座標のヘッダー矩形。ドロップ判定に使う</summary>
        Rect headerRect { get; }

        /// <summary>タブの可視状態の変化を検知して通知を発火する (差分がなければ何もしない)</summary>
        void NotifyTabVisibleChanged();

        /// <summary>現在の配置を永続化する。外部ウィンドウは no-op でよい</summary>
        void SavePlacement();

        /// <summary>
        /// タブバー状態の push を受け取る。titles=null はグループ非加入 (通常タイトル描画へ戻す)。
        /// ホスト (TabGroup) が状態変更時のみ呼ぶ契約で、毎フレームは呼ばれない
        /// </summary>
        void SetTabBarState(string[] titles, int activeIndex);
    }
}
