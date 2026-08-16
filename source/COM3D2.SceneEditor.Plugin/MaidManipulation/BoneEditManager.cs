using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン編集の状態管理。メイドごとの差分ストアを持ち、
    /// 着替えを検出して差分を破棄し、ロード完了後に残った差分を再適用する
    /// </summary>
    public class BoneEditManager : ManagerBase
    {
        private static BoneEditManager _instance;
        public static BoneEditManager instance => _instance ?? (_instance = new BoneEditManager());

        private BoneEditManager()
        {
        }

        /// <summary>ボーン編集ウィンドウの表示中だけ true。骨格線描画とボーンピックの有効化条件</summary>
        public bool editMode;

        public string targetSlotName = "";
        public Transform selectedBone;

        private readonly Dictionary<Maid, BoneEditStore> _stores = new Dictionary<Maid, BoneEditStore>();

        /// <summary>ロード完了エッジ検出用。前フレームのロード中フラグ</summary>
        private readonly Dictionary<Maid, bool> _wasLoading = new Dictionary<Maid, bool>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        /// <summary>前フレームでギズモをドラッグしていたか（終了フレームの取りこぼし対策）</summary>
        private bool _wasGizmoDragging;

        /// <summary>通常のオブジェクト選択が変わったことを検出するための前フレーム値</summary>
        private GameObject _lastSelectedObject;

        private List<SlotBoneNode> _boneTree = new List<SlotBoneNode>();
        private GameObject _boneTreeSource;

        /// <summary>揺れ物理探索結果のキャッシュ (ボーン単位)。ドラッグ中の毎フレーム探索を避ける</summary>
        private Transform _yureCheckedBone;
        private SlotYureTargets _yureTargets;

        public override void Init()
        {
            // ボーン選択中はギズモの操作対象を選択オブジェクトから差し替える。
            // SelectionManager は「ボーンヒットはメイドルートへ丸める」規約なので経由しない
            GizmoRenderer.externalTargetProvider = () =>
                editMode && selectedBone != null ? selectedBone.gameObject : null;
        }

        /// <summary>
        /// ボーンを選択し、Inspector 側の選択にも反映する。
        /// メイドポーズ定義があるボーンはひねり/曲げスライダー表示へ、
        /// それ以外は選択をメイドルートへ揃えて汎用回転スライダー表示にする
        /// </summary>
        public void SelectBone(Maid maid, Transform bone)
        {
            // bone が null のときは選択解除を兼ねるため、ガードより先に反映する
            selectedBone = bone;
            if (maid == null || bone == null)
            {
                return;
            }

            var def = MaidBoneSliderController.FindDef(bone.name);
            if (def != null)
            {
                selectionManager.SelectBone(maid, def);
            }
            else
            {
                selectionManager.Select(maid.gameObject);
            }

            // 選択オブジェクトの変更エッジで自分の選択を解除しないよう前フレーム値を揃える
            _lastSelectedObject = selectionManager.selectedObject;
        }

        /// <summary>
        /// スロット切替等でボーン選択を解除する。Inspector 側のポーズ定義選択が
        /// 残ると古いボーンのスライダーが表示され続けるため、そちらも揃えて落とす
        /// </summary>
        public void ClearBoneSelection()
        {
            selectedBone = null;
            if (selectionManager.hasBoneSelection)
            {
                // 同一オブジェクトの再選択はイベントを発火させずに定義選択だけ解除する
                selectionManager.Select(selectionManager.selectedObject);
            }
        }

        /// <summary>
        /// 選択中ボーンの基準回転。編集済みなら記録時の元値、未編集なら現在値
        /// (未編集 = オフセット 0 として扱う)
        /// </summary>
        private Quaternion GetSelectedBoneBaseRotation(Maid maid)
        {
            var store = FindStore(maid);
            var entry = store != null ? store.GetEntry(targetSlotName, selectedBone.name) : null;
            return entry != null ? entry.origRotation : selectedBone.localRotation;
        }

        /// <summary>選択中ボーンの基準回転からのオフセット角（±180 正規化済み）</summary>
        public Vector3 GetSelectedBoneOffset(Maid maid)
        {
            if (maid == null || selectedBone == null)
            {
                return Vector3.zero;
            }

            var baseRot = GetSelectedBoneBaseRotation(maid);
            var euler = (Quaternion.Inverse(baseRot) * selectedBone.localRotation).eulerAngles;
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        /// <summary>選択中ボーンの指定軸オフセット角を書き込み、差分ストアへ記録する</summary>
        public void SetSelectedBoneOffsetAxis(Maid maid, int axisIndex, float value)
        {
            if (maid == null || selectedBone == null)
            {
                return;
            }

            var offset = GetSelectedBoneOffset(maid);
            var baseRot = GetSelectedBoneBaseRotation(maid);
            offset[axisIndex] = value;
            selectedBone.localRotation = baseRot * Quaternion.Euler(offset);
            NotifyBoneEdited(maid, selectedBone);
        }

        /// <summary>角度を -180〜180 に正規化する</summary>
        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }

        /// <summary>メイドの差分ストア。無ければ作る</summary>
        public BoneEditStore GetStore(Maid maid)
        {
            BoneEditStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new BoneEditStore();
                _stores[maid] = store;
            }
            return store;
        }

        /// <summary>
        /// 既存の差分ストアを引くだけで新規生成はしない。
        /// 読み取りだけの用途 (プリセット記録など) で毎フレーム走査対象を増やさないために使う
        /// </summary>
        public BoneEditStore FindStore(Maid maid)
        {
            BoneEditStore store;
            return _stores.TryGetValue(maid, out store) ? store : null;
        }

        /// <summary>選択中スロットのボーンが編集されたことを記録する</summary>
        public void NotifyBoneEdited(Maid maid, Transform bone)
        {
            if (maid == null || bone == null || string.IsNullOrEmpty(targetSlotName))
            {
                return;
            }

            DisableYure(maid, bone);

            var fileName = SlotBoneManager.GetSlotItemFileName(maid, targetSlotName);
            GetStore(maid).RecordEdit(targetSlotName, fileName, bone);
        }

        /// <summary>
        /// ボーンを駆動している揺れ物理。関連物理が無ければ null。
        /// FindTargets は階層探索を伴うため、結果はボーン単位でキャッシュして使い回す。
        /// 関連物理は装着物が変わらない限り不変で、着替えるとボーンごと作り直されるため、
        /// Transform の同一性で判定すれば古い参照を掴み続けることはない
        /// </summary>
        public SlotYureTargets GetYureTargets(Maid maid, Transform bone)
        {
            if (bone != _yureCheckedBone)
            {
                _yureCheckedBone = bone;
                _yureTargets = SlotYureUtil.FindTargets(maid, targetSlotName, bone);
            }
            return _yureTargets;
        }

        /// <summary>
        /// 編集したボーンの揺れものを止める。揺れが動いていると編集した回転が
        /// 毎フレーム上書きされて操作にならないため、Inspector/ギズモからの編集で自動的に OFF にする。
        /// 履歴には残さない (編集モードへの自動遷移と同じく、操作の副作用として扱う)
        /// </summary>
        private void DisableYure(Maid maid, Transform bone)
        {
            var targets = GetYureTargets(maid, bone);
            if (targets != null && SlotYureUtil.GetYureState(targets))
            {
                SlotYureUtil.SetYureState(targets, false);
            }
        }

        /// <summary>
        /// 操作対象メイド × 選択スロットのボーンツリー。スロット obj が変わったら作り直し、
        /// 消えたボーンを掴んだままにしないよう選択も落とす。
        /// 副作用があるためプロパティではなくメソッドで公開する
        /// </summary>
        public List<SlotBoneNode> GetCurrentBoneTree()
        {
            var maid = MaidManipulateManager.instance.targetMaid;
            var obj = SlotBoneManager.GetSlotObject(maid, targetSlotName);

            // 破棄済み参照どうしの比較は等しくなるため、空でない状態で obj が消えた場合も作り直す
            if (obj != _boneTreeSource || (obj == null && _boneTree.Count > 0))
            {
                _boneTree = SlotBoneManager.BuildBoneTree(obj);
                _boneTreeSource = obj;
                selectedBone = null;
            }
            return _boneTree;
        }

        public override void Update()
        {
            ReleaseBoneOnObjectSelected();
            RecordGizmoDrag();
            UpdateStores();
        }

        /// <summary>
        /// ボーン選択中はギズモがそのボーンに固定されるため、通常のオブジェクト選択が
        /// 行われたらボーン選択を解除して従来のギズモ操作へ戻す
        /// </summary>
        private void ReleaseBoneOnObjectSelected()
        {
            var selectedObject = selectionManager.selectedObject;
            if (selectedObject != _lastSelectedObject)
            {
                _lastSelectedObject = selectedObject;
                selectedBone = null;
            }
        }

        /// <summary>
        /// ギズモは対象 Transform を直接書くため変更通知がない。ドラッグ中は毎フレーム記録する。
        /// GizmoRenderer と本 Manager の実行順によってはドラッグ終了フレームの最終値を
        /// 取りこぼすため、終了後 1 フレームも追記録する
        /// </summary>
        private void RecordGizmoDrag()
        {
            var maid = MaidManipulateManager.instance.targetMaid;
            if (!editMode || selectedBone == null || maid == null)
            {
                _wasGizmoDragging = false;
                return;
            }

            var isDragging = IsGizmoDragging(SceneViewManager.instance.gizmoRenderer)
                || IsGizmoDragging(GameViewManager.instance.gizmoRenderer);

            if (isDragging || _wasGizmoDragging)
            {
                // ドラッグ開始エッジで変更前状態を控える (初回フレームの微小移動分の誤差は許容)
                if (!_wasGizmoDragging)
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ボーン編集: " + selectedBone.name, new[] { selectedBone });

                    // ボーンを動かし始めたらメニューバーの編集モード (isEditMode) へ自動遷移する。
                    // 本クラスの editMode (ウィンドウ表示状態) とは別物。
                    // ドラッグ点・ボーンギズモ経由の遷移は MaidManipulateManager.Update が担う
                    MaidManipulateManager.instance.isEditMode = true;
                }
                NotifyBoneEdited(maid, selectedBone);
            }
            _wasGizmoDragging = isDragging;
        }

        private static bool IsGizmoDragging(GizmoRenderer gizmo)
        {
            return gizmo != null && gizmo.isDragging;
        }

        private void UpdateStores()
        {
            _deadMaids.Clear();

            foreach (var pair in _stores)
            {
                var maid = pair.Key;
                if (maid == null)
                {
                    _deadMaids.Add(maid);
                    continue;
                }

                // 呼出直後の初回ロードと、着替え (SetProp + AllProcPropSeqStart) の両方を待つ。
                // ストアが空でもフラグは必ず更新する (エッジ検出の取りこぼし防止)
                var isLoading = MaidManipulateManager.instance.IsLoading(maid) || maid.IsAllProcPropBusy;
                bool wasLoading;
                _wasLoading.TryGetValue(maid, out wasLoading);
                _wasLoading[maid] = isLoading;

                if (isLoading || !wasLoading || pair.Value.isEmpty)
                {
                    continue;
                }

                // アイテム変更はロード経由でしか起きないため、着替え検出と再適用は
                // ロード完了エッジでのみ行う (毎フレームの文字列比較を避ける)
                DiscardAndReapply(maid, pair.Value);
            }

            foreach (var maid in _deadMaids)
            {
                _stores.Remove(maid);
                _wasLoading.Remove(maid);
            }
        }

        private static void DiscardAndReapply(Maid maid, BoneEditStore store)
        {
            foreach (var slotName in SlotBoneManager.GetLoadedSlotNames(maid))
            {
                store.DiscardSlotIfItemChanged(
                    slotName, SlotBoneManager.GetSlotItemFileName(maid, slotName));
                store.ReapplySlot(slotName, SlotBoneManager.GetSlotObject(maid, slotName));
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _stores.Clear();
            _wasLoading.Clear();
            _boneTree.Clear();
            _boneTreeSource = null;
            selectedBone = null;
            _yureCheckedBone = null;
            _yureTargets = null;
        }
    }
}
