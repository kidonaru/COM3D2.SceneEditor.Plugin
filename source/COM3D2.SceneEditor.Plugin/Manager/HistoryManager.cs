using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 操作履歴 1 件の抽象。本体のスナップショット式と外部プラグインのデリゲート式を
    /// 同じ履歴リストに積むための共通インターフェース
    /// </summary>
    public interface IHistoryEntry
    {
        /// <summary>履歴一覧の表示名。登録時に HistoryManager が記録時刻を付与する</summary>
        string description { get; set; }
        /// <summary>対象消滅・着替え中などで今は適用できないとき false (エントリはスキップされる)</summary>
        bool canApply { get; }
        /// <summary>操作前の状態へ書き戻す (undo)</summary>
        void ApplyBefore();
        /// <summary>操作後の状態へ書き戻す (redo)</summary>
        void ApplyAfter();
    }

    /// <summary>操作履歴 1 件。操作前後のスナップショットを対で持つ</summary>
    public class HistoryEntry : IHistoryEntry
    {
        public string description { get; set; }
        public Maid maid;
        public HistoryScope scope;
        /// <summary>
        /// 同一スコープ内で対象を区別するキー (編集中のマテリアル・シェイプキー名など)。
        /// 確定待ちの集約判定にだけ使い、null なら区別しない
        /// </summary>
        public object targetKey;
        public IStateSnapshot before;
        public IStateSnapshot after;

        public bool canApply => before.CanApply(maid);

        public void ApplyBefore() => before.Apply(maid);
        public void ApplyAfter() => after.Apply(maid);
    }

    /// <summary>
    /// メイド操作の undo/redo。各操作は変更前に BeforeEdit を呼び、
    /// マウス解放のタイミングで操作後の状態と対にして 1 エントリへ確定する
    /// (ドラッグ中の連続変更を 1 件に集約する。MTE の RequestHistory と同じ考え方)
    /// </summary>
    public class HistoryManager : ManagerBase
    {
        private static HistoryManager _instance;
        public static HistoryManager instance => _instance ?? (_instance = new HistoryManager());

        private HistoryManager()
        {
        }

        private readonly List<IHistoryEntry> _entries = new List<IHistoryEntry>();

        /// <summary>最後に適用済みのエントリ位置。-1 は全て undo 済みまたは履歴なし</summary>
        public int currentIndex { get; private set; } = -1;

        public List<IHistoryEntry> entries => _entries;

        /// <summary>履歴が変化した (追加/undo/redo/ジャンプ/クリア)。ウィンドウ更新用</summary>
        public event Action onChanged;

        /// <summary>
        /// 内部操作 (BeforeEdit 経由) が値の変更を伴って 1 件確定した。
        /// タイムラインの自動キーフレーム登録が購読する。
        /// 外部プラグインの登録 (AddEntry 直接) や undo/redo では発火しない
        /// </summary>
        public event Action<HistoryEntry> onEditCommitted;

        /// <summary>確定待ちの操作。同一 (メイド, スコープ) の連続変更をまとめる</summary>
        private HistoryEntry _pending;

        /// <summary>undo/redo の適用中か。外部エントリからの再入を弾くのに使う</summary>
        private bool _isApplying;

        /// <summary>
        /// タイムラインモードか。シーン操作とタイムライン操作が 1 つのスタックに混ざると
        /// undo の対象が追えなくなるため、タイムライン読み込み中は履歴をタイムライン操作専用にする
        /// </summary>
        public bool isTimelineMode => MTEP.TimelineManager.instance.timeline != null;

        /// <summary>前フレームのモード。切り替わりを検出して履歴を捨てるのに使う</summary>
        private bool _wasTimelineMode;

        public bool canUndo => currentIndex >= 0 || (_pending != null && !isTimelineMode);
        public bool canRedo => currentIndex + 1 < _entries.Count;

        /// <summary>
        /// 状態を変更する操作の直前に呼ぶ。最初の呼び出し時点の状態を変更前として控える。
        /// ドラッグやスライダー操作中は毎回呼んでよい (記録済みボーンは 2 回目以降何もしない)。
        /// Pose スコープでは操作が変更するボーンを targetBones で渡す
        /// </summary>
        public void BeforeEdit(Maid maid, HistoryScope scope, string description,
            IEnumerable<Transform> targetBones = null)
        {
            BeforeEditCore(maid, scope, description, null,
                () => SnapshotFactory.Capture(maid, scope, targetBones),
                targetBones);
        }

        /// <summary>
        /// SnapshotFactory を通さず、呼び出し側がスナップショットを組み立てるオーバーロード。
        /// メイドに紐付かない対象 (モデルのマテリアル・演出の全体状態) 向け。
        /// targetKey が異なれば別の操作として確定待ちを切り替える。
        /// capture は確定待ちが無いときだけ評価される
        /// </summary>
        public void BeforeEdit(Maid maid, HistoryScope scope, string description,
            object targetKey, Func<IStateSnapshot> capture)
        {
            BeforeEditCore(maid, scope, description, targetKey, capture, null);
        }

        private void BeforeEditCore(Maid maid, HistoryScope scope, string description,
            object targetKey, Func<IStateSnapshot> capture, IEnumerable<Transform> targetBones,
            bool enterEditMode = true)
        {
            // 値を書き換える操作の直前に必ず通る場所なので、ここで編集モードへ入る。
            // 履歴が無効 (historyLimit <= 0) でも自動移行は必要なため、早期 return より前に置く
            if (enterEditMode)
            {
                AutoEditMode.Enter();
            }

            if ((maid == null && HistoryScopeUtils.RequiresMaid(scope))
                || config.historyLimit <= 0)
            {
                return;
            }

            if (_pending != null
                && (_pending.maid != maid
                    || _pending.scope != scope
                    || !Equals(_pending.targetKey, targetKey)))
            {
                CommitPending();
            }

            if (_pending == null)
            {
                var snapshot = capture();
                if (snapshot == null)
                {
                    return;
                }

                _pending = new HistoryEntry
                {
                    description = description,
                    maid = maid,
                    scope = scope,
                    targetKey = targetKey,
                    before = snapshot,
                };
            }
            else
            {
                _pending.before.AddBones(targetBones);
            }
        }

        /// <summary>
        /// 対象ボーンの列挙が重い操作向けのオーバーロード。
        /// 確定待ちが既にある間は対象集合が変わらない前提で provider を評価しない
        /// (全身ボーンのようにドラッグ中の毎フレーム走査が重い場合に使う)
        /// </summary>
        public void BeforeEdit(Maid maid, HistoryScope scope, string description,
            Func<IEnumerable<Transform>> targetBonesProvider)
        {
            // 確定待ちがあるなら BeforeEditCore を通っており編集モードにも入っているため、そのまま抜けてよい
            if (_pending != null && _pending.maid == maid && _pending.scope == scope)
            {
                return;
            }
            BeforeEdit(maid, scope, description, targetBonesProvider());
        }

        /// <summary>
        /// 編集モードへの自動移行をしない BeforeEdit。編集モードを**抜ける**処理の途中から呼ぶ用。
        /// 通常版は AutoEditMode.Enter() を通るが、抜ける途中だと
        /// SceneEditorHack.isPoseEditing を立て直して停止したベースの再生まで再開させてしまう
        /// (抜ける操作そのものが打ち消される)。
        /// 呼び出し元は既に編集モードの中で起きた編集を記録するので、移行は不要
        /// </summary>
        public void BeforeEditWhileLeavingEditMode(Maid maid, HistoryScope scope, string description,
            Func<IEnumerable<Transform>> targetBonesProvider)
        {
            if (_pending != null && _pending.maid == maid && _pending.scope == scope)
            {
                return;
            }
            // provider は全身ボーンの走査が重いので 1 回だけ評価する
            var targetBones = targetBonesProvider();
            BeforeEditCore(maid, scope, description, null,
                () => SnapshotFactory.Capture(maid, scope, targetBones),
                targetBones, enterEditMode: false);
        }

        /// <summary>
        /// モードの切り替わりを検出し、残った履歴を捨てる (別モードの操作になるため)。
        /// 読み込み直後の登録が同フレーム内で消されないよう、Update だけでなく登録時にも呼ぶ
        /// </summary>
        private void SyncTimelineMode()
        {
            var currentMode = isTimelineMode;
            if (currentMode != _wasTimelineMode)
            {
                _wasTimelineMode = currentMode;
                ClearHistory();
            }
        }

        public override void Update()
        {
            SyncTimelineMode();

            // マウスを離すまで確定を遅らせ、ドラッグ 1 回を 1 エントリにする
            if (_pending != null && !Input.GetMouseButton(0))
            {
                CommitPending();
            }
        }

        /// <param name="notify">確定を onEditCommitted で通知するか。プラグイン無効化時の掃き出しでは通知しない</param>
        private void CommitPending(bool notify = true)
        {
            var pending = _pending;
            _pending = null;

            if (pending.maid == null && HistoryScopeUtils.RequiresMaid(pending.scope))
            {
                return;
            }

            // 変更後は before と同じ対象集合をその時点の値で取り直す
            pending.after = pending.before.CaptureCurrent();

            // クリックのみで値が変わっていない操作 (ドラッグ点の選択等) は積まない
            if (pending.before.Approximately(pending.after))
            {
                return;
            }

            // タイムラインモードではシーン操作を履歴に積まないが、
            // 自動キーフレーム登録は変更の確定を頼りにするため通知だけは行う
            if (!isTimelineMode)
            {
                AddEntry(pending);
            }

            // AddEntry は適用中 (_isApplying) に受け付けないため、履歴に載らない操作は通知しない
            if (notify && !_isApplying)
            {
                onEditCommitted?.Invoke(pending);
            }
        }

        /// <summary>
        /// 確定済みエントリを履歴へ積む。外部プラグインの登録もここを通る。
        /// description には記録時刻を付与するため、呼び出し側は操作名だけを入れておく
        /// </summary>
        public void AddEntry(IHistoryEntry entry)
        {
            if (entry == null || config.historyLimit <= 0)
            {
                return;
            }

            // 適用中の登録は _entries を作り替えて適用ループの走査を壊すため受け付けない
            // (モード同期の ClearHistory も同じ理由で適用中は走らせない)
            if (_isApplying)
            {
                MTEUtils.LogWarning("履歴の適用中は登録できません: {0}", entry.description);
                return;
            }

            SyncTimelineMode();

            // 外部からの登録時に確定待ちの内部操作が残っていれば先に確定し、時系列を保つ
            if (_pending != null && _pending != entry)
            {
                CommitPending();
            }

            // タイムラインモードではタイムライン操作だけを履歴に残す
            if (isTimelineMode && !(entry is TimelineHistoryEntry))
            {
                return;
            }

            // 履歴一覧での識別用に記録時刻を付ける
            entry.description = string.Format(
                "[{0:HH:mm:ss}] {1}", DateTime.Now, entry.description);

            // redo 側を切り捨ててから追加する
            if (currentIndex + 1 < _entries.Count)
            {
                _entries.RemoveRange(currentIndex + 1, _entries.Count - currentIndex - 1);
            }
            _entries.Add(entry);

            while (_entries.Count > config.historyLimit)
            {
                _entries.RemoveAt(0);
            }
            currentIndex = _entries.Count - 1;

            onChanged?.Invoke();
        }

        public void Undo()
        {
            if (!BeginApply())
            {
                return;
            }

            try
            {
                while (currentIndex >= 0)
                {
                    var entry = _entries[currentIndex];
                    currentIndex--;
                    if (TryApply(entry, useBefore: true))
                    {
                        MTEUtils.LogDebug("元に戻す: {0}", entry.description);
                        return;
                    }
                    // 対象メイドが消えたエントリは飛ばして次を戻す
                }
            }
            finally
            {
                EndApply();
            }
        }

        public void Redo()
        {
            if (!BeginApply())
            {
                return;
            }

            try
            {
                while (currentIndex + 1 < _entries.Count)
                {
                    var entry = _entries[currentIndex + 1];
                    currentIndex++;
                    if (TryApply(entry, useBefore: false))
                    {
                        MTEUtils.LogDebug("やり直す: {0}", entry.description);
                        return;
                    }
                }
            }
            finally
            {
                EndApply();
            }
        }

        /// <summary>履歴ウィンドウからの直接ジャンプ。対象位置まで undo/redo を順に辿る</summary>
        public void RestoreTo(int index)
        {
            if (!BeginApply())
            {
                return;
            }

            try
            {
                index = Mathf.Clamp(index, -1, _entries.Count - 1);
                while (currentIndex > index)
                {
                    var entry = _entries[currentIndex];
                    currentIndex--;
                    LogIfSkipped(entry, TryApply(entry, useBefore: true));
                }
                while (currentIndex < index)
                {
                    var entry = _entries[currentIndex + 1];
                    currentIndex++;
                    LogIfSkipped(entry, TryApply(entry, useBefore: false));
                }
            }
            finally
            {
                EndApply();
            }
        }

        /// <summary>
        /// 適用処理の開始。確定待ちを片付け、適用中フラグを立てる。
        /// 外部エントリのクロージャから undo/redo/登録を呼び返されると
        /// 走査中の _entries が作り替わって壊れるため、再入は弾く
        /// </summary>
        private bool BeginApply()
        {
            if (_isApplying)
            {
                MTEUtils.LogWarning("履歴の適用中は undo/redo を実行できません");
                return false;
            }

            if (_pending != null)
            {
                CommitPending();
            }

            _isApplying = true;
            return true;
        }

        /// <summary>適用処理の終了。例外で抜けた場合も含めて必ず UI へ変化を通知する</summary>
        private void EndApply()
        {
            _isApplying = false;
            onChanged?.Invoke();
        }

        /// <summary>ジャンプ中に適用できなかったエントリを可視化する (黙って通過させない)</summary>
        private static void LogIfSkipped(IHistoryEntry entry, bool applied)
        {
            if (!applied)
            {
                MTEUtils.LogWarning("対象メイドが操作できないため履歴を飛ばしました: {0}",
                    entry.description);
            }
        }

        private static bool TryApply(IHistoryEntry entry, bool useBefore)
        {
            try
            {
                // canApply も外部プラグインの実装が入るため適用と同じ try で保護する
                if (!entry.canApply)
                {
                    return false;
                }

                // 戻した値もパラメータ変更と同じく編集モード外では書き戻されるため、当てる前に入る
                AutoEditMode.Enter();

                if (useBefore)
                {
                    entry.ApplyBefore();
                }
                else
                {
                    entry.ApplyAfter();
                }
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogError("履歴の適用に失敗しました: {0}", entry.description);
                MTEUtils.LogException(e);
                return false;
            }
        }

        public override void OnPluginDisable()
        {
            // 無効化中は Update が回らず確定待ちが滞留するため、この時点で確定する
            if (_pending != null)
            {
                CommitPending(notify: false);
            }
        }

        public void ClearHistory()
        {
            _entries.Clear();
            currentIndex = -1;
            _pending = null;
            onChanged?.Invoke();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーンをまたぐとメイドも状態も引き継がれないため履歴を捨てる
            ClearHistory();
        }
    }
}
