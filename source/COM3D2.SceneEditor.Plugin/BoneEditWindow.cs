using System;
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
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ボーン";

        // サイズは描画時にウィンドウ幅から求める
        private readonly GUIComboBox<string> _slotComboBox = new GUIComboBox<string>
        {
            getName = (slotName, _) => slotName,
        };

        /// <summary>表示中の 1 行。Hierarchy と同じく展開状態から組み立てる</summary>
        private struct Row
        {
            public SlotBoneNode node;
            public int depth;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _expanded = new HashSet<int>(); // Transform の GetInstanceID
        // _rows の組み直しが必要か。行の内容はボーンツリー・展開状態・検索語だけで決まるため、
        // この 3 つが変わったときにだけ立てればよい (OnGUI は 1 フレームに複数回走る)
        private bool _rowsDirty = true;
        // 組み直し済みのボーンツリー。BoneEditManager.GetCurrentBoneTree() はスロット obj が
        // 変わらない限り同一インスタンスを返すため、参照比較でツリー変化を検出できる
        private List<SlotBoneNode> _lastTree = null;
        private string _searchText = "";
        // 選択行を画面内へ送るスクロール量。次の描画で反映する
        private int _scrollToRow = -1;
        // 外部経路 (ビュー窓のボーンピック等) の選択変更検出用
        private Transform _lastSelectedBone;
        // 選択変更で表示したいボーン。行構築後に行位置を求めてスクロールする
        private Transform _pendingReveal;

        private static BoneEditManager boneEditManager => BoneEditManager.instance;

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
        /// ボーンツリー。Hierarchy と同じく行を固定高で手動配置し、
        /// 表示範囲外の行を描画から省き、行の組み立ても変化があったときだけ行う。
        /// 行まわりの作りは HierarchyWindow とほぼ対になっているため、片方を直すときは他方も見ること
        /// </summary>
        private void DrawBoneTree(Maid target)
        {
            var tree = boneEditManager.GetCurrentBoneTree();
            if (tree.Count == 0)
            {
                view.DrawLabel("ボーンがありません", -1, ROW_HEIGHT);
                return;
            }

            if (tree != _lastTree)
            {
                _lastTree = tree;
                _rowsDirty = true;
            }

            // 展開状態を確定させてから行を構築する (Hierarchy と同じ流れ)
            DetectExternalSelection();
            if (_rowsDirty)
            {
                BuildRows(tree);
            }
            ResolvePendingReveal();

            view.DrawTextField(_searchText, -1, ROW_HEIGHT, value =>
            {
                _searchText = value;
                _rowsDirty = true;
            });

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            var listRect = view.GetDrawRect(-1, -1);
            if (listRect.height <= 0f)
            {
                return;
            }

            ApplyScrollToRow(listRect.height);

            // 行位置とスクロール量をずらさないよう、Hierarchy と同じく padding なしで描く
            var savedPadding = view.padding;
            view.padding = Vector2.zero;

            var store = boneEditManager.GetStore(target);
            var contentWidth = Mathf.Max(listRect.width - ScrollBarWidth, 0f);
            var contentHeight = _rows.Count * ROW_HEIGHT;
            // 内容矩形は毎フレーム行数から与える。EndScrollView の高さ書き戻しは
            // 次フレームのここで上書きされるためスクロール範囲は保たれる
            view.BeginScrollView(
                listRect.width, listRect.height,
                new Rect(0f, 0f, contentWidth, contentHeight), false, true);
            {
                // 表示範囲に入っている行だけ描く
                var firstRow = Mathf.Max((int)(view.scrollPosition.y / ROW_HEIGHT), 0);
                var lastRow = Mathf.Min(
                    (int)((view.scrollPosition.y + listRect.height) / ROW_HEIGHT) + 1, _rows.Count - 1);
                for (var i = firstRow; i <= lastRow && i < _rows.Count; i++)
                {
                    DrawRow(target, store, _rows[i], i, contentWidth);
                }
            }
            view.EndScrollView();

            view.padding = savedPadding;
        }

        /// <summary>
        /// ビュー窓のボーンピック等、外部経路の選択変更を検出して祖先を展開する。
        /// 行位置は BuildRows 後でないと確定しないため、ここでは対象を覚えるだけ
        /// </summary>
        private void DetectExternalSelection()
        {
            var selected = boneEditManager.selectedBone;
            if (selected == _lastSelectedBone)
            {
                return;
            }
            _lastSelectedBone = selected;
            _pendingReveal = selected;

            if (selected == null)
            {
                return;
            }

            // ツリー外の Transform が混ざっても展開集合に余分な ID が入るだけで害はない
            for (var parent = selected.parent; parent != null; parent = parent.parent)
            {
                if (_expanded.Add(parent.GetInstanceID()))
                {
                    _rowsDirty = true;
                }
            }
        }

        /// <summary>
        /// 選択変更で覚えた対象の行位置を求めてスクロール予約する。
        /// 検索フィルタ等で行に出ていない場合は何もしない (予約だけ破棄する)
        /// </summary>
        private void ResolvePendingReveal()
        {
            if (_pendingReveal == null)
            {
                return;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].node.transform == _pendingReveal)
                {
                    _scrollToRow = i;
                    break;
                }
            }
            _pendingReveal = null;
        }

        /// <summary>展開状態と検索条件から、実際に表示する行を組み立てる</summary>
        private void BuildRows(List<SlotBoneNode> tree)
        {
            _rowsDirty = false;
            _rows.Clear();
            var searching = !string.IsNullOrEmpty(_searchText);
            foreach (var node in tree)
            {
                AddRows(node, 0, searching);
            }
        }

        private void AddRows(SlotBoneNode node, int depth, bool searching)
        {
            if (node.transform == null)
            {
                return;
            }

            // 検索中は一致するものだけフラット表示
            var matched = !searching ||
                node.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            if (matched)
            {
                _rows.Add(new Row { node = node, depth = searching ? 0 : depth });
            }

            if (searching || _expanded.Contains(node.transform.GetInstanceID()))
            {
                foreach (var child in node.children)
                {
                    AddRows(child, depth + 1, searching);
                }
            }
        }

        /// <summary>index 行目を描く。行の位置は行番号から直接決めるため currentPos を毎回置き直す</summary>
        private void DrawRow(Maid target, BoneEditStore store, Row row, int index, float contentWidth)
        {
            var node = row.node;
            // 行はツリーが作り直されるまでキャッシュされるため、破棄済みが残りうる。
            // Hierarchy と違って定期更新が無く放っておくと空白行が残り続けるので、
            // 見つけた時点で組み直しを予約する (反復中なのでその場では組み直さない)
            if (node.transform == null)
            {
                _rowsDirty = true;
                return;
            }

            view.currentPos = new Vector2(row.depth * IndentWidth, index * ROW_HEIGHT);
            view.BeginHorizontal();
            {
                if (node.children.Count > 0)
                {
                    var isExpanded = _expanded.Contains(node.transform.GetInstanceID());
                    if (view.DrawButton(isExpanded ? "-" : "+", ToggleWidth, ROW_HEIGHT))
                    {
                        ToggleExpanded(node.transform);
                    }
                }
                else
                {
                    // 子がなくてもラベルの開始位置は揃える
                    view.DrawEmpty(ToggleWidth, ROW_HEIGHT);
                }

                var isEdited = store.GetEntry(boneEditManager.targetSlotName, node.name) != null;
                var label = isEdited ? node.name + " *" : node.name;
                var labelWidth = contentWidth - view.currentPos.x;
                var isSelected = node.transform == boneEditManager.selectedBone;
                if (view.DrawButton(
                    label, labelWidth, ROW_HEIGHT, true,
                    isSelected ? Color.cyan : Color.white, GUIView.gsLabel))
                {
                    // 編集 UI は Inspector 側に出すため、選択を Inspector にも反映する
                    boneEditManager.SelectBone(target, node.transform);
                    _lastSelectedBone = node.transform;
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 展開状態を切り替える。行の描画ループから呼ばれるため、ここでは _rows を組み直さない
        /// (組み直すと反復中のリストが縮んで添字が範囲外になる)。次フレームの BuildRows で反映される
        /// </summary>
        private void ToggleExpanded(Transform transform)
        {
            var id = transform.GetInstanceID();
            if (!_expanded.Remove(id))
            {
                _expanded.Add(id);
            }
            _rowsDirty = true;
        }

        /// <summary>選択で予約された行がスクロール範囲外なら、見える位置まで送る</summary>
        private void ApplyScrollToRow(float viewHeight)
        {
            if (_scrollToRow < 0)
            {
                return;
            }

            var top = _scrollToRow * ROW_HEIGHT;
            var bottom = top + ROW_HEIGHT;

            var scrollPosition = view.scrollPosition;
            if (scrollPosition.y > top)
            {
                scrollPosition.y = top;
            }
            else if (scrollPosition.y + viewHeight < bottom)
            {
                scrollPosition.y = bottom - viewHeight;
            }
            view.scrollPosition = scrollPosition;

            _scrollToRow = -1;
        }
    }
}
