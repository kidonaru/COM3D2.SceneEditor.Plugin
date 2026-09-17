using System;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグイン向けの操作履歴 API。
    /// 本体の undo/redo (キー・履歴ウィンドウ) と同じ履歴リストへ外部の操作を積める。
    ///
    /// 契約:
    /// - Register は「確定済み」の操作 1 件を登録する。ドラッグ中の連続変更を
    ///   1 件へまとめるのは呼び出し側の責務 (操作確定時に 1 回だけ呼ぶ)
    /// - BeforeEdit は確定前の操作向け。値を書く直前に毎回呼び、確定 (マウス解放) は本体に任せる
    /// - undo/redo クロージャは冪等であり、他エントリとの順序に依存しないこと
    ///   (履歴ウィンドウのジャンプで連続適用される)
    /// - undo/redo/canApply の中から Register/Undo/Redo を呼び返さないこと。
    ///   適用中の再入は履歴を壊すため受け付けず、警告ログを出して無視する
    /// - onChanged を購読したら不要になった時点で必ず解除すること。
    ///   履歴は常駐するため、解除を怠るとハンドラが掴んだ参照ごと残り、
    ///   ウィンドウ再生成のたびに購読すると多重発火になる
    /// - シーン遷移で履歴は全クリアされる。クロージャが掴んだ参照もそこで解放される
    ///
    /// 外部からはアセンブリをハード参照せず、Type.GetType + Delegate.CreateDelegate で
    /// 掴めるよう、シグネチャは標準型のみで構成している
    /// </summary>
    public static class HistoryAPI
    {
        /// <summary>
        /// 確定済みの操作を 1 件登録する。
        /// タイムライン読み込み中 (タイムラインモード) はシーン操作を履歴に残さないため登録は無視される
        /// </summary>
        /// <param name="description">履歴ウィンドウに表示する操作名</param>
        /// <param name="undo">操作前の状態へ書き戻す処理</param>
        /// <param name="redo">操作後の状態へ書き戻す処理</param>
        /// <param name="canApply">対象消滅等で今は適用できないとき false を返す判定。null なら常に適用可</param>
        public static void Register(
            string description, Action undo, Action redo, Func<bool> canApply = null)
        {
            if (undo == null || redo == null)
            {
                MTEUtils.LogError("HistoryAPI.Register: undo/redo は必須です: {0}", description);
                return;
            }

            HistoryManager.instance.AddEntry(
                new DelegateHistoryEntry(description, undo, redo, canApply));
        }

        /// <summary>
        /// 変更前の状態を控える (SceneEditor 内部の BeforeEdit と同じ経路)。
        /// 値を書き換える操作の直前に毎回呼んでよく、同じ targetKey の連続変更は
        /// マウス解放時に 1 件へまとめて確定する。編集モードへの自動移行も伴う。
        /// シーンモードでは undo/redo に積まれ、タイムラインモードでは履歴には積まれず
        /// 自動キーフレーム登録の契機になる (内部の編集と同じ扱い)
        /// </summary>
        /// <param name="description">履歴ウィンドウに表示する操作名</param>
        /// <param name="targetKey">確定待ちを区別するキー (エフェクト名等)。null なら区別しない</param>
        /// <param name="capture">現在の状態を文字列で返す。記録できないときは null を返す</param>
        /// <param name="apply">文字列の状態を書き戻す (undo/redo)</param>
        /// <param name="canApply">対象消滅等で今は適用できないとき false。null なら常に適用可</param>
        public static void BeforeEdit(
            string description, string targetKey,
            Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            if (capture == null || apply == null)
            {
                MTEUtils.LogError("HistoryAPI.BeforeEdit: capture/apply は必須です: {0}", description);
                return;
            }

            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.External, description, targetKey,
                () => ExternalStateSnapshot.Capture(capture, apply, canApply));
        }

        public static void Undo() => HistoryManager.instance.Undo();
        public static void Redo() => HistoryManager.instance.Redo();

        public static bool canUndo => HistoryManager.instance.canUndo;
        public static bool canRedo => HistoryManager.instance.canRedo;

        /// <summary>
        /// 履歴が変化した (追加/undo/redo/ジャンプ/クリア)。外部 UI の更新用。
        /// 購読したら不要になった時点で必ず解除すること
        /// </summary>
        public static event Action onChanged
        {
            add => HistoryManager.instance.onChanged += value;
            remove => HistoryManager.instance.onChanged -= value;
        }
    }

    /// <summary>外部プラグインが登録するデリゲート式の履歴エントリ</summary>
    internal class DelegateHistoryEntry : IHistoryEntry
    {
        private readonly Action _undo;
        private readonly Action _redo;
        private readonly Func<bool> _canApply;

        public string description { get; set; }
        public bool canApply => _canApply == null || _canApply();

        public DelegateHistoryEntry(
            string description, Action undo, Action redo, Func<bool> canApply)
        {
            this.description = description;
            _undo = undo;
            _redo = redo;
            _canApply = canApply;
        }

        public void ApplyBefore() => _undo();
        public void ApplyAfter() => _redo();
    }
}
