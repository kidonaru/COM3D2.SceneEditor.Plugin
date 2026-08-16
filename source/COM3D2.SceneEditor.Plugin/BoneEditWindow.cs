using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スロット別ボーンの可視化・編集ウィンドウ。
    /// 表示中だけビュー窓に骨格線が出て、関節クリックでボーンを選べるようになる。
    /// ボーン一覧は Hierarchy と同じ UI (展開/折りたたみ + 検索 + 行仮想化) で表示する
    /// </summary>
    public class BoneEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903371;

        private const float IndentWidth = 14f;
        private const float ToggleWidth = 20f;
        private const float ScrollBarWidth = 16f;

        private static readonly int ResetButtonWidth = 110;

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

        private readonly GUITreeView<SlotBoneNode> _treeView = new GUITreeView<SlotBoneNode>();
        // 外部経路 (ビュー窓のボーンピック等) の選択変更検出用
        private Transform _lastSelectedBone;
        // ラベル生成デリゲートから参照する、描画中のメイドと編集ストア。
        // GUITreeView にはゲーム固有の型を渡せないため、描画直前にここへ置いてから使う。
        //
        // 【前提】これが有効なのは _treeView.Draw() の実行中だけ。
        // 現状 onSelected は Draw() の中からしか発火しないため成立している。
        // このウィンドウに _treeView.HandleKeyboard() を足す場合は、描画外から
        // onSelected が飛んできて前フレームの値を掴む経路が生まれるため、
        // ここをフィールド経由ではなく引数渡しに変える必要がある
        private Maid _drawingTarget;
        private BoneEditStore _drawingStore;

        private static BoneEditManager boneEditManager => BoneEditManager.instance;

        /// <summary>
        /// ツリービューにボーンツリーのたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupTreeView()
        {
            _treeView.rowHeight = ROW_HEIGHT;
            _treeView.indentWidth = IndentWidth;
            _treeView.toggleWidth = ToggleWidth;
            _treeView.scrollBarWidth = ScrollBarWidth;

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

            DrawBoneVisibleToggle();
            DrawSlotSelector(target);
            view.DrawHorizontalLine();
            DrawResetButtons(target);
            DrawBoneTree(target);
        }

        /// <summary>メニューバーと同じボーン表示トグル。編集モードに関わらず切り替えられる</summary>
        private void DrawBoneVisibleToggle()
        {
            var manager = MaidManipulateManager.instance;
            view.DrawToggle("ボーン表示", manager.isBoneVisible, 100, ROW_HEIGHT,
                true, value => manager.isBoneVisible = value);
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
