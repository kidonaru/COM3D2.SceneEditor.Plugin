using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スロット別ボーンの可視化・編集ウィンドウ。
    /// 表示中だけビュー窓に骨格線が出て、関節クリックでボーンを選べるようになる。
    /// ボーン一覧の行まわり (展開/折りたたみ・検索・行仮想化) は GUITreeView に委譲し、
    /// ここではスロット選択とボーンツリーの提供、リセット操作、
    /// PartsEdit 互換プリセットの保存/適用を担う
    /// </summary>
    public class BoneEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903371;

        private static readonly int ResetButtonWidth = 110;
        private static readonly int TAB_WIDTH = 80;

        /// <summary>編集対象の種別タブ</summary>
        private enum TargetTabType
        {
            メイド,
            モデル,
        }

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum BoneTabType
        {
            編集,
            プリセット,
        }

        private BoneTabType _tabType = BoneTabType.編集;

        private static BoneEditWindow _instance = null;
        public static BoneEditWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BoneEditWindow();
                }
                return _instance;
            }
        }

        private BoneEditWindow()
        {
            SetupTreeView();
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ボーン";

        // サイズは描画時にウィンドウ幅から求める
        private readonly GUIComboBox<string> _slotComboBox = new GUIComboBox<string>
        {
            getName = (slotName, _) => slotName,
        };

        /// <summary>スロットコンボの項目 (「なし」+ 装着中スロット)</summary>
        private readonly List<string> _slotItems = new List<string>();

        private readonly GUIComboBox<ExternalModelEntry> _modelComboBox = new GUIComboBox<ExternalModelEntry>
        {
            getName = (entry, _) => entry.displayName,
        };

        // 対象種別タブをメイド選択行より上に置くため、基底の選択行は使わず自前で描く
        protected override bool showMaidSelector => false;

        // PartsEdit 互換プリセット。一覧は表示時とタブ切替・保存/削除後に更新する
        private List<string> _presetNames = new List<string>();
        /// <summary>プリセット一覧の絞り込み語</summary>
        private string _presetSearchText = "";

        private readonly GUITreeView<SlotBoneNode> _treeView = new GUITreeView<SlotBoneNode>();
        // 外部経路 (ビュー窓のボーンピック等) の選択変更検出用
        private Transform _lastSelectedBone;
        // ラベル生成デリゲートが参照する、描画中のメイドと編集ストア。
        // GUITreeView へゲーム固有の型を渡せないため、Draw() 直前にここへ置く。
        // 有効なのは Draw() 実行中だけ (onSelected は現状 Draw() 内でしか発火しないため成立)。
        // _treeView.HandleKeyboard() を足す場合は前フレームの値を掴む経路が生まれるため、
        // フィールド経由ではなく引数渡しに変えること
        private Maid _drawingTarget;
        private BoneEditStore _drawingStore;

        private static BoneEditManager boneEditManager => BoneEditManager.instance;

        /// <summary>差分ストアのキー。メイドはスロット名、モデルは固定キー</summary>
        private string activeSlotKey => boneEditManager.isModelMode
            ? BoneEditManager.ModelSlotKey
            : boneEditManager.targetSlotName;

        /// <summary>描画中の対象の差分ストア。モデルモードでは対象モデルのストア</summary>
        private BoneEditStore GetActiveStore(Maid target)
        {
            return boneEditManager.isModelMode
                ? boneEditManager.GetModelStore(boneEditManager.targetModel)
                : boneEditManager.GetStore(target);
        }

        /// <summary>
        /// ボーンをたどる起点。メイドは選択スロットの obj、モデルはモデルルート。
        /// null なら操作対象が未選択 (プリセット適用・リセットの可否判定に使う)
        /// </summary>
        private GameObject GetActiveRootObject(Maid target)
        {
            return boneEditManager.isModelMode
                ? boneEditManager.targetModel
                : SlotBoneManager.GetSlotObject(target, boneEditManager.targetSlotName);
        }

        /// <summary>
        /// ツリービューにボーンツリーのたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupTreeView()
        {
            // インデント幅等は GUITreeView の既定値をそのまま使う
            _treeView.rowHeight = ROW_HEIGHT;

            // ID は Transform のものを使う。ビュー窓のピックで飛んでくるのも Transform のため、
            // Reveal / Expand と突き合わせるにはこれで揃えておく必要がある
            _treeView.getId = node => node.transform.GetInstanceID();
            _treeView.getName = node => node.name;
            _treeView.isAlive = node => node.transform != null;
            _treeView.getChildCount = node => node.children.Count;
            _treeView.getChild = (node, i) => node.children[i];

            _treeView.getLabel = node =>
            {
                var isEdited = _drawingStore != null &&
                    _drawingStore.GetEntry(activeSlotKey, node.name) != null;
                return isEdited ? node.name + " *" : node.name;
            };
            _treeView.getLabelColor = node =>
                node.transform == boneEditManager.selectedBone ? Color.cyan : Color.white;
            _treeView.isSelected = node => node.transform == boneEditManager.selectedBone;
            _treeView.onSelected = node =>
            {
                // 編集 UI は Inspector 側に出すため、選択を Inspector にも反映する
                if (boneEditManager.isModelMode)
                {
                    boneEditManager.SelectModelBone(node.transform);
                }
                else
                {
                    boneEditManager.SelectBone(_drawingTarget, node.transform);
                }
                _lastSelectedBone = node.transform;
            };
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.boneEditPosX;
            y = config.boneEditPosY;
            width = config.boneEditWidth;
            height = config.boneEditHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.boneEditPosX = x;
            config.boneEditPosY = y;
            config.boneEditWidth = width;
            config.boneEditHeight = height;
        }

        public override bool savedVisible
        {
            get => config.boneEditVisible;
            set => config.boneEditVisible = value;
        }

        protected override void OnShowChanged(bool visible)
        {
            // 表示中だけ骨格線とボーンピックを有効化する
            boneEditManager.editMode = visible;

            if (visible)
            {
                RefreshPresetList();
            }
        }

        /// <summary>
        /// プリセット一覧を読み直す。
        /// 保存先は PartsEdit 本体と共用でメイド用とモデル用が混在するため、対象種別で絞る
        /// </summary>
        private void RefreshPresetList()
        {
            _presetNames = PartsEditPresetIO.GetPresetNames(!boneEditManager.isModelMode);
        }

        protected override void DrawMaidContent(Maid target)
        {
            var prevType = boneEditManager.targetType;
            var tab = DrawInnerTabs(
                prevType == BoneEditTargetType.Model ? TargetTabType.モデル : TargetTabType.メイド,
                TAB_WIDTH);
            boneEditManager.targetType = tab == TargetTabType.モデル
                ? BoneEditTargetType.Model
                : BoneEditTargetType.Maid;
            if (boneEditManager.targetType != prevType)
            {
                // 選択ボーンだけ落とす。targetModel / targetSlotName はタブを往復しても
                // 復帰できるよう意図的に保持する
                boneEditManager.ClearBoneSelection();

                // プリセット一覧は対象種別で中身が変わる。絞り込み語も持ち越さない
                _presetSearchText = "";
                RefreshPresetList();
            }

            if (boneEditManager.isModelMode)
            {
                DrawModelContent();
                return;
            }

            // showMaidSelector を切っているため、メイドモードではここで選択行を描く
            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディが読み込まれていません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            if (target.IsAllProcPropBusy)
            {
                view.DrawLabel("プロパティ適用中...", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            // スロット選択はプリセットの適用先も兼ねるため、タブの上に共通で置く
            DrawHeaderRow(target);
            DrawSlotSelector(target);

            DrawContentTabs(target);
        }

        /// <summary>
        /// 編集 / プリセットタブとその中身。対象種別で描き分ける箇所は
        /// 各メソッドが activeSlotKey / GetActiveStore / GetActiveRootObject で吸収する
        /// </summary>
        private void DrawContentTabs(Maid target)
        {
            var prevTab = _tabType;
            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);
            if (_tabType != prevTab && _tabType == BoneTabType.プリセット)
            {
                // フォルダを直接編集された場合もタブを開き直せば一覧に反映される
                RefreshPresetList();
            }

            if (_tabType == BoneTabType.プリセット)
            {
                DrawPresetContent(target);
            }
            else
            {
                DrawResetButtons(target);
                DrawBoneTree(target);
            }
        }

        /// <summary>
        /// モデルモードの描画。対象は外部プラグイン (ModelProviderHost) が提供するモデル一覧から選ぶ
        /// </summary>
        private void DrawModelContent()
        {
            var models = ModelProviderHost.GetModels();
            var manager = boneEditManager;

            view.BeginHorizontal();
            {
                view.DrawLabel("対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var comboWidth = CalcLabeledComboWidth(view);
                _modelComboBox.buttonSize = new Vector2(comboWidth, ROW_HEIGHT);
                _modelComboBox.contentSize = new Vector2(comboWidth, 300f);

                _modelComboBox.items = models;
                _modelComboBox.currentIndex = models.FindIndex(
                    entry => entry.obj == manager.targetModel);
                // defaultName は非 null だと選択項目より優先されるため、未選択時だけ設定する
                _modelComboBox.defaultName = _modelComboBox.currentIndex >= 0
                    ? null
                    : models.Count == 0 ? "モデルがありません" : "選択してください";
                _modelComboBox.onSelected = (entry, _) =>
                {
                    manager.targetModel = entry.obj;
                    manager.ClearBoneSelection();
                };
                _modelComboBox.DrawButton(view);
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            if (manager.targetModel == null)
            {
                view.DrawLabel(models.Count == 0
                    ? "対応プラグインの配置モデルがありません"
                    : "操作対象のモデルを選択してください", -1, ROW_HEIGHT);
                return;
            }

            // メイドモードと同じくヘッダーはタブの上。対象は targetModel から引かれる
            DrawHeaderRow(null);
            DrawContentTabs(null);
        }

        /// <summary>
        /// タブの上の共通ヘッダー。
        /// メニューバーと同じボーン表示トグル (編集モードに関わらず切り替えられる) と、
        /// どのタブからでも押せるプリセット保存ボタンを並べる
        /// </summary>
        private void DrawHeaderRow(Maid target)
        {
            var manager = MaidManipulateManager.instance;
            var store = GetActiveStore(target);
            var rootObj = GetActiveRootObject(target);
            var slotKey = activeSlotKey;

            view.BeginHorizontal();
            {
                view.DrawToggle("ボーン表示", manager.isBoneVisible, 100, ROW_HEIGHT,
                    true, value => manager.isBoneVisible = value);

                // 保存対象は選択中の対象の編集差分。差分が無いときは押させない
                if (view.DrawButton("プリセット保存", 110, ROW_HEIGHT,
                    rootObj != null && store.GetEntries(slotKey).Count > 0))
                {
                    // ポップアップ表示中の対象変更を検出できるよう、押した時点の対象を控える
                    SaveBonePresetPopupWindow.Show(
                        presetName => SavePreset(target, rootObj, slotKey, presetName));
                }
            }
            view.EndLayout();
        }

        /// <summary>アイテムが載っているスロットから編集対象を選ぶ。編集済みスロットには * を付ける</summary>
        private void DrawSlotSelector(Maid target)
        {
            var store = boneEditManager.GetStore(target);

            // 骨格線を消せるよう、先頭に「なし」(スロット未選択) を足す
            _slotItems.Clear();
            _slotItems.Add(BoneEditManager.NoSlotName);
            _slotItems.AddRange(SlotBoneManager.GetLoadedSlotNames(target));

            view.BeginHorizontal();
            {
                view.DrawLabel("スロット", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var comboWidth = CalcLabeledComboWidth(view);
                _slotComboBox.buttonSize = new Vector2(comboWidth, ROW_HEIGHT);
                _slotComboBox.contentSize = new Vector2(comboWidth, 300f);

                _slotComboBox.items = _slotItems;
                _slotComboBox.getName = (slotName, _) =>
                {
                    if (string.IsNullOrEmpty(slotName))
                    {
                        return "なし";
                    }
                    return store.GetEntries(slotName).Count > 0 ? slotName + " *" : slotName;
                };
                _slotComboBox.currentIndex = _slotItems.IndexOf(boneEditManager.targetSlotName);
                // 「なし」は常に一覧にあるため、ここに来るのは選択中スロットの
                // アイテムが外れて一覧から消えたときだけ
                _slotComboBox.defaultName = _slotComboBox.currentIndex >= 0
                    ? null
                    : "スロットが見つかりません";
                _slotComboBox.onSelected = (slotName, _) =>
                {
                    boneEditManager.targetSlotName = slotName;
                    boneEditManager.ClearBoneSelection();
                };
                _slotComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// プリセットタブ。検索欄と
        /// PartsEdit 互換プリセット (UnityInjector\Config\PartsEdit\*.xml) の一覧を描画する。
        /// 保存ボタンはどのタブからも押せるようヘッダー側にある
        /// </summary>
        private void DrawPresetContent(Maid target)
        {
            view.DrawTextField("検索", LABEL_WIDTH, _presetSearchText, -1, ROW_HEIGHT,
                value => _presetSearchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            DrawPresetList(target);
            view.EndScrollView();
        }

        /// <summary>保存済みプリセットを一覧表示する。名前を押すと選択中の対象へ適用する</summary>
        private void DrawPresetList(Maid target)
        {
            if (_presetNames.Count == 0)
            {
                view.DrawLabel("保存されたプリセットはありません", -1, ROW_HEIGHT);
                return;
            }

            const int deleteButtonWidth = 50;
            // スクロールバー分は viewRect が既に差し引かれているため、削除ボタンと間隔だけ引く
            var nameButtonWidth = view.viewRect.width - view.padding.x * 2
                - deleteButtonWidth - view.margin;

            var canApply = GetActiveRootObject(target) != null;
            var matched = 0;
            foreach (var presetName in _presetNames)
            {
                if (!string.IsNullOrEmpty(_presetSearchText) && presetName
                    .IndexOf(_presetSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                matched++;

                view.BeginHorizontal();
                {
                    // 適用先はスロット / モデル。未選択では適用できないため押させない
                    if (view.DrawButton(presetName, nameButtonWidth, ROW_HEIGHT, canApply))
                    {
                        LoadPreset(target, presetName);
                    }

                    if (view.DrawButton("削除", deleteButtonWidth, ROW_HEIGHT))
                    {
                        DialogPopupWindow.ShowConfirmDialog(
                            "プリセット「" + presetName + "」を削除しますか？",
                            () =>
                            {
                                PartsEditPresetIO.Delete(presetName);
                                RefreshPresetList();
                            });
                    }
                }
                view.EndLayout();
            }

            if (matched == 0)
            {
                view.DrawLabel("一致するプリセットはありません", -1, ROW_HEIGHT);
            }
        }

        /// <summary>選択中プリセットのボーン TRS を選択中の対象へ適用する</summary>
        private void LoadPreset(Maid target, string presetName)
        {
            var data = PartsEditPresetIO.Load(presetName);
            if (data == null)
            {
                ToastManager.Show("プリセットの読み込みに失敗しました", ToastType.Error);
                return;
            }

            var rootObj = GetActiveRootObject(target);
            var store = GetActiveStore(target);
            boneEditManager.BeginEditHistory(target, "プリセットをロード: " + presetName,
                PartsEditPresetIO.ResolveBones(rootObj, data));

            var applied = boneEditManager.isModelMode
                ? PartsEditPresetIO.ApplyModel(rootObj, data, store)
                : PartsEditPresetIO.Apply(target, boneEditManager.targetSlotName, data, store);
            ToastManager.Show(string.Format("プリセットを適用しました ({0} ボーン)", applied),
                ToastType.Success);
        }

        /// <summary>
        /// ポップアップで確定した名前で、選択中の対象の編集差分を保存する。
        /// 名前検証と上書き確認はポップアップ側で済んでいる。
        /// captured 系の引数はポップアップを開いた時点の対象 (表示中に変わり得るため)
        /// </summary>
        private void SavePreset(Maid target, GameObject capturedRootObj, string capturedSlotKey,
            string presetName)
        {
            var isModel = boneEditManager.isModelMode;

            // ポップアップ表示中に操作対象が変わっていたら、別の対象の状態を保存しないよう中止する。
            // 差分ストアも押した時点の対象から引き、現在の選択とのズレを持ち込まない
            BoneEditStore store;
            if (isModel)
            {
                // 破棄済みどうしの比較は等しくなるため、消失と切替を別々に見る
                if (capturedRootObj == null || boneEditManager.targetModel != capturedRootObj)
                {
                    DialogPopupWindow.ShowDialog("対象のモデルが変わったため保存を中止しました");
                    return;
                }
                store = boneEditManager.GetModelStore(capturedRootObj);
            }
            else
            {
                if (maidManager.targetMaid != target)
                {
                    DialogPopupWindow.ShowDialog("操作対象が変わったため保存を中止しました");
                    return;
                }
                store = boneEditManager.GetStore(target);
            }

            // 着替えを挟むと該当スロットの編集差分は破棄される。
            // ポップアップ表示中に起きると空のプリセットを書いてしまうため中止する
            if (store.GetEntries(capturedSlotKey).Count == 0)
            {
                DialogPopupWindow.ShowDialog("編集内容が失われたため保存を中止しました");
                return;
            }

            var saved = isModel
                ? PartsEditPresetIO.SaveModel(capturedRootObj, presetName, store)
                : PartsEditPresetIO.Save(target, capturedSlotKey, presetName, store);
            if (!saved)
            {
                ToastManager.Show("プリセットの保存に失敗しました", ToastType.Error);
                return;
            }
            RefreshPresetList();
            ToastManager.Show("プリセットを保存しました: " + presetName, ToastType.Success);
        }

        private void DrawResetButtons(Maid target)
        {
            var store = GetActiveStore(target);
            var slotKey = activeSlotKey;
            var bone = boneEditManager.selectedBone;
            var hasBoneEdit = bone != null && store.GetEntry(slotKey, bone.name) != null;

            // 2 ボタンが並ぶ行。最小幅まで縮めても押せるよう、残り幅の半分を上限にする
            var buttonWidth = Mathf.Min(
                ResetButtonWidth,
                (view.viewRect.width - view.padding.x * 2 - view.margin) * 0.5f);

            view.BeginHorizontal();
            {
                if (view.DrawButton("ボーンをリセット", buttonWidth, ROW_HEIGHT, hasBoneEdit))
                {
                    boneEditManager.BeginEditHistory(target,
                        "ボーンをリセット: " + bone.name, new[] { bone });
                    store.ResetBone(slotKey, bone);
                }

                // 一括リセットの単位はメイドなら選択スロット、モデルならモデル全体
                var isModel = boneEditManager.isModelMode;
                var resetAllLabel = isModel ? "モデルをリセット" : "スロットをリセット";
                if (view.DrawButton(resetAllLabel, buttonWidth, ROW_HEIGHT,
                    store.GetEntries(slotKey).Count > 0))
                {
                    var rootObj = GetActiveRootObject(target);
                    var historyTarget = isModel ? rootObj.name : slotKey;
                    boneEditManager.BeginEditHistory(target,
                        resetAllLabel + ": " + historyTarget,
                        GetSlotEditedBones(store, slotKey, rootObj));
                    store.ResetSlot(slotKey, rootObj);
                }
            }
            view.EndLayout();
        }

        /// <summary>スロット内の編集済みボーン。リセットの記録対象に使う</summary>
        private static IEnumerable<Transform> GetSlotEditedBones(
            BoneEditStore store, string slotName, GameObject slotObj)
        {
            foreach (var entry in store.GetEntries(slotName))
            {
                var bone = SlotBoneManager.FindBone(slotObj, entry.boneName);
                if (bone != null)
                {
                    yield return bone;
                }
            }
        }

        /// <summary>
        /// ボーンツリー。行まわりは GUITreeView に委譲し、
        /// ここではスロットのツリーを渡すことと検索欄の配置だけを行う
        /// </summary>
        private void DrawBoneTree(Maid target)
        {
            var tree = boneEditManager.GetCurrentBoneTree();
            if (tree.Count == 0)
            {
                view.DrawLabel("ボーンがありません", -1, ROW_HEIGHT);
                return;
            }

            // GetCurrentBoneTree() はスロット obj が変わらない限り同じインスタンスを返すため、
            // SetRoots の参照比較だけでツリーの作り直しを検出できる
            _treeView.SetRoots(tree);

            // 展開状態を確定させてから描く
            DetectExternalSelection();

            view.DrawTextField(_treeView.searchText, -1, ROW_HEIGHT,
                value => _treeView.searchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // ラベル生成から参照するため、描画に入る前に置いておく
            _drawingTarget = target;
            _drawingStore = GetActiveStore(target);

            _treeView.Draw(view, view.GetDrawRect(-1, -1));
        }

        /// <summary>
        /// ビュー窓のボーンピック等、外部経路の選択変更を検出して祖先を展開する。
        /// 行位置は行構築後でないと確定しないため、ここでは表示予約だけしておく
        /// </summary>
        private void DetectExternalSelection()
        {
            var selected = boneEditManager.selectedBone;
            if (selected == _lastSelectedBone)
            {
                return;
            }
            _lastSelectedBone = selected;

            if (selected == null)
            {
                return;
            }

            _treeView.Reveal(selected.GetInstanceID());

            // ツリー外の Transform が混ざっても展開集合に余分な ID が入るだけで害はない
            for (var parent = selected.parent; parent != null; parent = parent.parent)
            {
                _treeView.Expand(parent.GetInstanceID());
            }
        }
    }
}
