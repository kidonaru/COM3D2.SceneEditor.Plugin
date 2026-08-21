using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド操作機能の統括。呼出メイドの追跡と操作対象の選択を一元管理する
    /// </summary>
    public class MaidManipulateManager : ManagerBase
    {
        private static MaidManipulateManager _instance = null;
        public static MaidManipulateManager instance
            => _instance ?? (_instance = new MaidManipulateManager());

        private MaidManipulateManager()
        {
        }

        /// <summary>このプラグインが Activate したメイド。ゲーム側で消えた分は毎フレーム除去する</summary>
        public List<Maid> calledMaids = new List<Maid>();

        /// <summary>呼出後ロード完了待ちのメイド。完了時に配置して表示する</summary>
        private List<Maid> _loadingMaids = new List<Maid>();

        /// <summary>ロード完了後に SceneView を寄せる対象。操作対象の切替に引きずられないよう実体で持つ</summary>
        private Maid _focusRequestMaid = null;

        /// <summary>フォーカス待ちのメイド。配置直後はバウンズが古いため 1 フレーム置いて寄せる</summary>
        private Maid _pendingFocusMaid = null;

        /// <summary>
        /// 指定メイドのロード完了時に SceneView をそこへ寄せるよう予約する。
        /// メイド呼出ウィンドウからの明示的な呼出と、シーンプリセットの適用完了が呼び元。
        /// 複数回予約された場合は後の予約で上書きされ、最後に予約したメイドへ寄る
        /// </summary>
        public void RequestFocusOnLoaded(Maid maid)
        {
            _focusRequestMaid = maid;
        }

        /// <summary>フォーカス予約を捨てる（無効化・シーン遷移でメイドの実体が入れ替わるため）</summary>
        private void ClearPendingFocus()
        {
            _focusRequestMaid = null;
            _pendingFocusMaid = null;
        }

        /// <summary>解除されたメイドへ寄らないよう、そのメイドのフォーカス予約だけ捨てる</summary>
        private void ClearPendingFocus(Maid maid)
        {
            if (_focusRequestMaid == maid)
            {
                _focusRequestMaid = null;
            }
            if (_pendingFocusMaid == maid)
            {
                _pendingFocusMaid = null;
            }
        }

        /// <summary>呼出後のロード（プロップ適用）が終わっていないメイドか</summary>
        public bool IsLoading(Maid maid)
        {
            return _loadingMaids.Contains(maid);
        }

        /// <summary>メイドの表示状態。Maid.Visible ではなく遠方への退避で制御する</summary>
        private MaidVisibilityController _visibilityController = new MaidVisibilityController();

        /// <summary>画面に出ているか（退避していないか）</summary>
        public bool IsVisible(Maid maid)
        {
            return !_visibilityController.IsHidden(maid);
        }

        /// <summary>表示 / 非表示を切り替える</summary>
        public void SetVisible(Maid maid, bool visible)
        {
            _visibilityController.SetHidden(maid, !visible);
        }

        /// <summary>見かけ上の位置。退避中は戻り先を返す</summary>
        public Vector3 GetLogicalPosition(Maid maid)
        {
            return _visibilityController.GetLogicalPosition(maid);
        }

        /// <summary>退避中のメイドの戻り先を差し替える（退避契約は MaidVisibilityController 参照）</summary>
        public void SetRestorePosition(Maid maid, Vector3 pos)
        {
            _visibilityController.SetRestorePosition(maid, pos);
        }

        /// <summary>
        /// 呼出済みメイドを配置モードの位置へ並べる。
        /// 退避中のメイドを動かすと画面に出てしまうため、戻り先だけ書き換える
        /// </summary>
        public void ApplyPlacement(MaidPlacementPreset.PresetType type)
        {
            for (var i = 0; i < calledMaids.Count; i++)
            {
                var maid = calledMaids[i];
                if (maid == null)
                {
                    continue;
                }

                var pos = MaidPlacementPreset.GetPosition(type, i);
                if (_visibilityController.IsHidden(maid))
                {
                    _visibilityController.SetRestorePosition(maid, pos);
                }
                else
                {
                    MaidPlacementPreset.WarpTo(maid, pos);
                }
            }
        }

        // 代入は必ず targetMaid setter か ClearTargetMaidSilently を経由すること。
        // 直接代入すると外部プラグインへの選択変更通知 (MaidSelectHost) が漏れる。
        // なお UpdateLoadingMaids は代入を伴わない再通知のため NotifyMaidChanged を直接呼ぶ
        private Maid _targetMaid = null;

        /// <summary>
        /// 操作対象のメイド。メイド系ウィンドウ全体で共有する
        /// </summary>
        public Maid targetMaid
        {
            get
            {
                // 解放済みのメイドを掴み続けないようにする
                if (!IsAlive(_targetMaid))
                {
                    _targetMaid = null;
                }
                return _targetMaid;
            }
            set
            {
                var previous = _targetMaid;
                _targetMaid = value;

                // ギズモは Inspector の選択オブジェクトに出るため、対象メイドを選んだら
                // 選択も揃えてそのまま動かせるようにする
                if (value != null)
                {
                    selectionManager.Select(value.gameObject);
                }
                // 解除されたメイドを掴み続けない。ストックの Maid は GameObject ごと
                // 使い回されるため、放置すると別メイドの Transform を誤って操作してしまう。
                // 他のオブジェクトを選んでいるなら横取りしない
                else if (previous != null && previous.gameObject != null &&
                    selectionManager.selectedObject == previous.gameObject)
                {
                    selectionManager.ClearSelection();
                }

                // 外部プラグインへはギズモ選択の同期が終わってから通知する
                if (previous != value)
                {
                    MaidSelectHost.NotifyMaidChanged(value);
                }
            }
        }

        /// <summary>ボーン回転ギズモ。修飾キーで表示グループが切り替わる</summary>
        public MaidBoneGizmoController boneGizmoController = new MaidBoneGizmoController();

        /// <summary>IK 終端と頭部のドラッグ点</summary>
        public MaidDragPointController dragPointController = new MaidDragPointController();

        /// <summary>指ブレンド。ウィンドウの手指/足指タブから操作する</summary>
        public MaidFingerBlendController fingerBlendController = new MaidFingerBlendController();

        /// <summary>指関節の個別ドラッグ点。指ウィンドウの「個別編集」トグルが ON の間だけ出す</summary>
        public MaidFingerDragPointController fingerDragPointController = new MaidFingerDragPointController();

        /// <summary>指の個別編集。指ウィンドウのトグルと連動する</summary>
        public bool isFingerEditMode { get; set; }

        /// <summary>IK 固定。IK ウィンドウから操作する</summary>
        public MaidIKHoldController ikHoldController = new MaidIKHoldController();

        /// <summary>胸の揺れもの ON/OFF。手付けした胸を物理に上書きさせないために持つ</summary>
        public MaidMuneYureController muneYureController = new MaidMuneYureController();

        /// <summary>視線の向け先。表情ウィンドウの視線タブから操作する</summary>
        public MaidLookController lookController = new MaidLookController();

        /// <summary>髪・スカートの重力。着替えで作り直された揺れものへ焼き直すため常駐させる</summary>
        public MaidGravityController gravityController = new MaidGravityController();

        private bool _isEditMode;

        /// <summary>編集モード。メニューバーのトグルと連動する。ボーンを動かし始めると自動で ON になる</summary>
        public bool isEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value)
                {
                    return;
                }
                _isEditMode = value;

                // IK 固定はモーション停止中しか効かないため、モードへ入った時点で
                // 固定中のメイドを停止させてすぐ効くようにする
                if (value)
                {
                    // 編集開始時点のポーズを基準にできるよう、呼出中の全メイドのモーションを止める
                    foreach (var maid in calledMaids)
                    {
                        MaidMotionState.StopMotion(maid);
                    }
                    ikHoldController.OnEditModeStarted();
                }
            }
        }

        /// <summary>ボーン表示。メニューバーのトグルと連動し、白丸とボーンギズモの出し分けに使う</summary>
        public bool isBoneVisible { get; set; } = true;

        /// <summary>操作対象として扱える状態か（実体が残っているか）。非表示中も操作対象に残す</summary>
        private static bool IsAlive(Maid maid)
        {
            return maid != null && maid.body0 != null;
        }

        public override void Update()
        {
            UpdateMaidLoading();

            var activeMaid = targetMaid;

            // 退避中のメイドは画面外に居るうえ、動かしても表示に戻す際に戻り先へ
            // 上書きされて編集が消えるため、座標を触る操作の対象から外す
            var movableMaid = IsVisible(activeMaid) ? activeMaid : null;

            // ギズモ・ドラッグ点は「ボーン表示」ON のときだけ出す。
            // 編集モード OFF でも表示・操作でき、掴んだ時点で編集モードへ入る
            var isBoneEditing = isBoneVisible;

            // ドラッグ点・ボーンギズモを掴んだら編集モードへ自動遷移する
            // (ボーン編集ウィンドウの選択ボーンギズモは BoneEditManager.RecordGizmoDrag が担う)
            if (MaidDragBoneTracker.draggingBoneName != null
                || boneGizmoController.grabbedBoneName != null)
            {
                isEditMode = true;
            }

            // 非表示の間はギズモコンポーネントを付けたままにしない
            // (呼出済みの全メイドへ常時アタッチされ、描画・ログのコストが残るため)
            boneGizmoController.SetTarget(isBoneEditing ? movableMaid : null);
            boneGizmoController.Update(isBoneEditing);

            dragPointController.SetTarget(isBoneEditing ? movableMaid : null);

            fingerDragPointController.SetTarget(
                isBoneEditing && isFingerEditMode ? movableMaid : null);

            fingerBlendController.SetTarget(activeMaid);

            // マウス追従の注視点をカーソル位置へ動かす（対象メイド以外も追従させる）
            lookController.Update();

            // 着替え完了・めくれで揺れものが作り直されたら重力を焼き直す
            gravityController.Update();
        }

        /// <summary>
        /// メイドのロード完了とプリセットの保留適用を進める。
        /// 通常は Update() から呼ばれるが、UI 無効中のシーンプリセット自動ロードでは
        /// マネージャの Update が回らないため、プラグイン本体からこれだけ直接呼ばれる
        /// </summary>
        public void UpdateMaidLoading()
        {
            // 消滅したメイドを追跡リストから外す（シーン遷移や外部プラグインでの解除に追従）
            calledMaids.RemoveAll(m => !IsAlive(m));

            AdoptActiveMaids();

            UpdateLoadingMaids();

            // デフォルト配置 (UpdateLoadingMaids) の後に呼び、プリセットの
            // 位置・ポーズを最終値にする
            ScenePresetManager.UpdatePendingApplies();
        }

        /// <summary>
        /// ゲーム側が既に出しているメイドを追跡へ取り込む。
        /// フォトモードが最初から出しているメイドや、呼出ウィンドウで
        /// 「既にアクティブなので呼び直さない」と判定されたメイドは呼出経路を
        /// 通らないため、ここで拾わないとシーンプリセットの保存対象
        /// （calledMaids）から丸ごと漏れてポーズも表情も記録されない。
        /// 解除したメイドは CharacterMgr.Deactivate がその場でスロットを空けるので、
        /// 次のフレームで拾い直してしまうことはない。
        /// シーン種別では絞らない。フォトモード以外でもプラグインを有効にすれば
        /// その場のメイドを編集・保存できる方を優先しており、代わりに
        /// プリセット適用時の解除・編集モードの停止・配置プリセットの対象にもなる
        /// </summary>
        private void AdoptActiveMaids()
        {
            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
                {
                    continue;
                }

                TrackCalledMaid(maid);
            }
        }

        /// <summary>追跡リストへの登録窓口。二重登録を防ぐためここを通す</summary>
        private void TrackCalledMaid(Maid maid)
        {
            if (maid != null && !calledMaids.Contains(maid))
            {
                calledMaids.Add(maid);
            }
        }

        public override void LateUpdate()
        {
            // ゲーム側のアニメ・物理より後に解いて固定位置を最終値にする
            ikHoldController.LateUpdate();
        }

        /// <summary>
        /// ロード完了したメイドを選択中の配置モードの位置へ置いてから表示する。
        /// 位置は calledMaids 内の並び順で決める
        /// </summary>
        private void UpdateLoadingMaids()
        {
            UpdatePendingFocus();

            for (var i = _loadingMaids.Count - 1; i >= 0; i--)
            {
                var maid = _loadingMaids[i];
                if (!IsAlive(maid))
                {
                    _loadingMaids.RemoveAt(i);

                    // 実体を失ったメイドをゲストへ掴ませ続けないよう解除を通知する。
                    // targetMaid getter の自動クリアは setter を通らず通知が漏れる
                    if (maid == _targetMaid)
                    {
                        ClearTargetMaidSilently();
                    }
                    continue;
                }

                if (maid.IsAllProcPropBusy || !maid.body0.isLoadedBody)
                {
                    continue;
                }

                // ゲーム側のデフォルトモーション再生（Maid.LateUpdate）は初回ロードの
                // 一度きりで、再呼出時はモーションが空のまま出てきてポーズ変更も
                // 効かなくなるため、未再生ならここで立ちモーションを流す
                if (string.IsNullOrEmpty(maid.body0.LastAnimeFN))
                {
                    maid.CrossFade("maid_stand01.anm", additive: false, loop: true);
                    maid.FaceAnime("通常", 1f, 0);
                }

                // 退避を解くと戻り先へ瞬間移動するので、配置位置を戻り先にしてから解く
                var placementType = (MaidPlacementPreset.PresetType)config.maidPlacementMode;
                var index = calledMaids.IndexOf(maid);
                if (index >= 0)
                {
                    _visibilityController.SetRestorePosition(
                        maid, MaidPlacementPreset.GetPosition(placementType, index));
                }
                _visibilityController.SetHidden(maid, false);

                _loadingMaids.RemoveAt(i);

                // 呼出直後の通知はロード中に流れており、ゲスト側のメイド一覧
                // (GetReadyMaidList) にはまだ載っていないため取りこぼされる。
                // ロードが片付いた時点で改めて流し直す
                if (maid == _targetMaid)
                {
                    MaidSelectHost.NotifyMaidChanged(maid);
                }
            }

            // 予約したメイド自身のロードが片付いてから寄せる
            if (_focusRequestMaid != null && !_loadingMaids.Contains(_focusRequestMaid))
            {
                _pendingFocusMaid = _focusRequestMaid;
                _focusRequestMaid = null;
            }
        }

        /// <summary>
        /// 予約済みのフォーカスを実行する。配置（ワープ）と同じフレームでは
        /// レンダラーのバウンズが退避先のままなので、次フレームまで待ってから寄せる
        /// </summary>
        private void UpdatePendingFocus()
        {
            var maid = _pendingFocusMaid;
            if (maid == null)
            {
                return;
            }
            _pendingFocusMaid = null;

            if (IsAlive(maid))
            {
                SceneViewWindow.instance.FocusOn(maid.gameObject);
            }
        }

        /// <summary>
        /// ストックメイドを空きスロットへ呼び出す。失敗時は null を返しログを残す
        /// </summary>
        public Maid CallMaid(int stockIndex)
        {
            try
            {
                var stockMaid = characterMgr.GetStockMaid(stockIndex);
                if (stockMaid == null)
                {
                    MTEUtils.LogWarning("ストックメイドが見つかりません: index={0}", stockIndex);
                    return null;
                }

                // 既にアクティブ（ロード済み）なら呼び直さない。非表示中も対象。
                // ロード待ちの間も二重 Activate を避けるため呼び直さない
                if (_loadingMaids.Contains(stockMaid) ||
                    (stockMaid.body0 != null && stockMaid.body0.isLoadedBody))
                {
                    // Update を待たずに追跡へ載せ、呼出直後の保存でも取りこぼさない
                    TrackCalledMaid(stockMaid);
                    targetMaid = stockMaid;
                    MaidCallWindow.instance.OnMaidCalled(stockMaid);
                    return stockMaid;
                }

                var slotNo = FindFreeActiveSlot();
                if (slotNo < 0)
                {
                    MTEUtils.LogWarning("メイドの呼出枠に空きがありません");
                    return null;
                }

                var maid = characterMgr.Activate(slotNo, stockIndex, false, false);
                if (maid == null)
                {
                    MTEUtils.LogWarning("メイドの呼出に失敗しました: stockIndex={0}", stockIndex);
                    return null;
                }

                // 呼出でメイドの実体が増えると既存エントリのボーン参照が意味を失うため履歴を捨てる
                // (ロードを伴うため undo 対象にしない。設計判断は仕様書を参照)。
                // 呼出が成立した後に捨てる。失敗や呼び直し不要の分岐では履歴を残す
                HistoryManager.instance.ClearHistory();
                MTEUtils.Log("メイド呼出のため操作履歴をクリアしました");

                // 退避に失敗しても追跡から漏れて画面外に取り残さないよう、先に登録する
                if (!_loadingMaids.Contains(maid))
                {
                    _loadingMaids.Add(maid);
                }

                TrackCalledMaid(maid);

                // ロード中の見た目（裸・原点湧き）を出さないため、遠方へ退避してロードし
                // 完了後に Update() 側で配置してから表示する
                characterMgr.CharaVisible(slotNo, true, false);
                _visibilityController.SetHidden(maid, true);
                maid.AllProcPropSeqStart();

                // 呼び出した直後から各ウィンドウで操作できるよう、そのまま操作対象にする。
                // ロード完了前でも位置はギズモ側が毎フレーム読むので配置後に追従する
                targetMaid = maid;
                MaidCallWindow.instance.OnMaidCalled(maid);
                return maid;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return null;
            }
        }

        /// <summary>アクティブスロットの空き番号を探す。全て埋まっていれば -1</summary>
        private int FindFreeActiveSlot()
        {
            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                if (characterMgr.GetMaid(i) == null)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>呼出したメイドを解除する。対象中なら選択も外す</summary>
        public void ReleaseMaid(Maid maid)
        {
            if (maid == null)
            {
                return;
            }

            // 解除でメイドごと消えると既存エントリが適用不能になるため履歴を捨てる
            HistoryManager.instance.ClearHistory();
            MTEUtils.Log("メイド解除のため操作履歴をクリアしました");

            if (targetMaid == maid)
            {
                targetMaid = null;
            }
            calledMaids.Remove(maid);
            _loadingMaids.Remove(maid);
            ClearPendingFocus(maid);
            // ストックの Maid は使い回されるため、退避したまま返さない
            _visibilityController.SetHidden(maid, false);

            // 固定状態とチェーンも残さない（使い回しの Maid へ持ち越さない）
            ikHoldController.Release(maid);
            // 揺れフラグも持ち越さない（ストックの Maid は使い回される）
            muneYureController.Release(maid);
            // 視線の向け先も持ち越さない（ストックの Maid は使い回される）
            lookController.Release(maid);
            // 重力も持ち越さない（ストックの Maid は使い回される）
            gravityController.Release(maid);
            // 指の開き/握り/ロックも持ち越さない（ストックの Maid は使い回される）
            fingerBlendController.Release(maid);

            // ストックの Maid は解除後も同一インスタンスが使い回されるため、
            // 停止クリップ名や適用記録を残すと再呼出時に古いモーションへ戻してしまう
            MaidMotionState.Release(maid);
            // 常駐ポーズクリップも破棄する (ストックの Maid の Animation に残り続けるため)
            MaidPoseFileManager.ReleaseClip(maid);

            try
            {
                for (var i = 0; i < characterMgr.GetMaidCount(); i++)
                {
                    if (characterMgr.GetMaid(i) == maid)
                    {
                        characterMgr.Deactivate(i, false);
                        return;
                    }
                }
                // アクティブスロットに居ない（シーン既存など）場合は非表示化のみ
                maid.Visible = false;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// シーンに残したもの（ギズモ・ドラッグ点）と、メイドを跨いで持ち越すと
        /// 不整合になる状態（停止クリップ名）をまとめて捨てる
        /// </summary>
        private void DestroyAll()
        {
            boneGizmoController.Destroy();
            dragPointController.Destroy();
            fingerDragPointController.Destroy();
            fingerBlendController.Destroy();
            ikHoldController.Destroy();
            muneYureController.Destroy();
            lookController.Destroy();
            gravityController.Destroy();
            MaidMotionState.Clear();
            MaidPoseFileManager.ClearClips();
        }

        public override void OnPluginDisable()
        {
            DestroyAll();

            // 無効化中は Update() が止まりロード完了を拾えないため、
            // 退避したまま取り残さないようすべて元の位置へ戻して追跡を捨てる
            _visibilityController.Clear();
            _loadingMaids.Clear();
            ClearPendingFocus();
            ScenePresetManager.ClearPendingApplies();

            ClearTargetMaidSilently();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            DestroyAll();
            calledMaids.Clear();
            _loadingMaids.Clear();
            ClearPendingFocus();
            // 遷移先では実体が入れ替わるため、保留中のプリセット適用も捨てる
            ScenePresetManager.ClearPendingApplies();
            // 遷移先ではメイドの実体ごと入れ替わるため、戻さず追跡だけ捨てる
            _visibilityController.Discard();
            // 遷移先では実体が入れ替わるので選択も捨てる
            ClearTargetMaidSilently();
        }

        /// <summary>
        /// setter の後片付け (ギズモ選択の同期) を通さずに選択だけ捨てる。
        /// 無効化・シーン遷移の一括後片付け用。選択解除の契約は守る必要があるため、
        /// 外部プラグインへの通知だけは行う (setter バイパスで通知が漏れると
        /// ゲストが破棄済みの Maid を掴み続ける)
        /// </summary>
        private void ClearTargetMaidSilently()
        {
            var previous = _targetMaid;
            _targetMaid = null;

            if (previous != null)
            {
                MaidSelectHost.NotifyMaidChanged(null);
            }
        }
    }
}
