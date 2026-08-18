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
                    _drawingStore.GetEntry(boneEditManager.targetSlotName, node.name) != null;
                return isEdited ? node.name + " *" : node.name;
            };
            _treeView.getLabelColor = node =>
                node.transform == boneEditManager.selectedBone ? Color.cyan : Color.white;
            _treeView.isSelected = node => node.transform == boneEditManager.selectedBone;
            _treeView.onSelected = node =>
            {
                // 編集 UI は Inspector 側に出すため、選択を Inspector にも反映する
                boneEditManager.SelectBone(_drawingTarget, node.transform);
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

        /// <summary>プリセット一覧を読み直す</summary>
        private void RefreshPresetList()
        {
            _presetNames = PartsEditPresetIO.GetPresetNames();
        }

        protected override void DrawMaidContent(Maid target)
        {
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
        /// タブの上の共通ヘッダー。
        /// メニューバーと同じボーン表示トグル (編集モードに関わらず切り替えられる) と、
        /// どのタブからでも押せるプリセット保存ボタンを並べる
        /// </summary>
        private void DrawHeaderRow(Maid target)
        {
            var manager = MaidManipulateManager.instance;
            var store = boneEditManager.GetStore(target);
            var slotName = boneEditManager.targetSlotName;
            var slotSelected = SlotBoneManager.GetSlotObject(target, slotName) != null;

            view.BeginHorizontal();
            {
                view.DrawToggle("ボーン表示", manager.isBoneVisible, 100, ROW_HEIGHT,
                    true, value => manager.isBoneVisible = value);

                // 保存対象は選択中スロットの編集差分。差分が無いときは押させない
                if (view.DrawButton("プリセット保存", 110, ROW_HEIGHT,
                    slotSelected && store.GetEntries(slotName).Count > 0))
                {
                    SaveBonePresetPopupWindow.Show(
                        presetName => SavePreset(target, slotName, presetName));
                }
            }
            view.EndLayout();
        }

        /// <summary>アイテムが載っているスロットから編集対象を選ぶ。編集済みスロットには * を付ける</summary>
        private void DrawSlotSelector(Maid target)
        {
            var slotNames = SlotBoneManager.GetLoadedSlotNames(target);
            var store = boneEditManager.GetStore(target);

            view.BeginHorizontal();
            {
                view.DrawLabel("スロット", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var comboWidth = CalcLabeledComboWidth(view);
                _slotComboBox.buttonSize = new Vector2(comboWidth, ROW_HEIGHT);
                _slotComboBox.contentSize = new Vector2(comboWidth, 300f);

                _slotComboBox.items = slotNames;
                _slotComboBox.getName = (slotName, _) =>
                    store.GetEntries(slotName).Count > 0 ? slotName + " *" : slotName;
                _slotComboBox.currentIndex = slotNames.IndexOf(boneEditManager.targetSlotName);
                // defaultName は非 null だと選択項目より優先されるため、未選択時だけ設定する
                _slotComboBox.defaultName = _slotComboBox.currentIndex >= 0
                    ? null
                    : slotNames.Count == 0 ? "スロットがありません" : "選択してください";
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
            var slotName = boneEditManager.targetSlotName;
            var slotSelected = SlotBoneManager.GetSlotObject(target, slotName) != null;

            view.DrawTextField("検索", LABEL_WIDTH, _presetSearchText, -1, ROW_HEIGHT,
                value => _presetSearchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            DrawPresetList(target, slotName, slotSelected);
            view.EndScrollView();
        }

        /// <summary>保存済みプリセットを一覧表示する。名前を押すと選択中スロットへ適用する</summary>
        private void DrawPresetList(Maid target, string slotName, bool slotSelected)
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
                    // 適用先はスロット。未選択では適用できないため押させない
                    if (view.DrawButton(presetName, nameButtonWidth, ROW_HEIGHT, slotSelected))
                    {
                        LoadPreset(target, slotName, presetName);
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

        /// <summary>選択中プリセットのボーン TRS を選択中スロットへ適用する</summary>
        private void LoadPreset(Maid target, string slotName, string presetName)
        {
            var data = PartsEditPresetIO.Load(presetName);
            if (data == null)
            {
                ToastManager.Show("プリセットの読み込みに失敗しました", ToastType.Error);
                return;
            }

            var slotObj = SlotBoneManager.GetSlotObject(target, slotName);
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose,
                "プリセットをロード: " + presetName,
                PartsEditPresetIO.ResolveBones(slotObj, data));
            var applied = PartsEditPresetIO.Apply(
                target, slotName, data, boneEditManager.GetStore(target));
            ToastManager.Show(string.Format("プリセットを適用しました ({0} ボーン)", applied),
                ToastType.Success);
        }

        /// <summary>
        /// ポップアップで確定した名前で、選択中スロットの編集差分を保存する。
        /// 名前検証と上書き確認はポップアップ側で済んでいる
        /// </summary>
        private void SavePreset(Maid target, string slotName, string presetName)
        {
            // ポップアップ表示中に操作対象が変わっていたら、別メイドの状態を保存しないよう中止する
            if (maidManager.targetMaid != target)
            {
                DialogPopupWindow.ShowDialog("操作対象が変わったため保存を中止しました");
                return;
            }

            // 着替えを挟むと該当スロットの編集差分は破棄される。
            // ポップアップ表示中に起きると空のプリセットを書いてしまうため中止する
            var store = boneEditManager.GetStore(target);
            if (store.GetEntries(slotName).Count == 0)
            {
                DialogPopupWindow.ShowDialog("編集内容が失われたため保存を中止しました");
                return;
            }

            if (!PartsEditPresetIO.Save(target, slotName, presetName, store))
            {
                ToastManager.Show("プリセットの保存に失敗しました", ToastType.Error);
                return;
            }
            RefreshPresetList();
            ToastManager.Show("プリセットを保存しました: " + presetName, ToastType.Success);
        }

        private void DrawResetButtons(Maid target)
        {
            var store = boneEditManager.GetStore(target);
            var slotName = boneEditManager.targetSlotName;
            var bone = boneEditManager.selectedBone;
            var hasBoneEdit = bone != null && store.GetEntry(slotName, bone.name) != null;

            // 2 ボタンが並ぶ行。最小幅まで縮めても押せるよう、残り幅の半分を上限にする
            var buttonWidth = Mathf.Min(
                ResetButtonWidth,
                (view.viewRect.width - view.padding.x * 2 - view.margin) * 0.5f);

            view.BeginHorizontal();
            {
                if (view.DrawButton("ボーンをリセット", buttonWidth, ROW_HEIGHT, hasBoneEdit))
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose,
                        "ボーンをリセット: " + bone.name, new[] { bone });
                    store.ResetBone(slotName, bone);
                }

                if (view.DrawButton("スロットをリセット", buttonWidth, ROW_HEIGHT,
                    store.GetEntries(slotName).Count > 0))
                {
                    var slotObj = SlotBoneManager.GetSlotObject(target, slotName);
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose,
                        "スロットをリセット: " + slotName, GetSlotEditedBones(store, slotName, slotObj));
                    store.ResetSlot(slotName, slotObj);
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
            _drawingStore = boneEditManager.GetStore(target);

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
