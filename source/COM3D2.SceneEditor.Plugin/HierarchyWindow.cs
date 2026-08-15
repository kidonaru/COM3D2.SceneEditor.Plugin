using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーン上の GameObject ツリーを表示するウィンドウ。
    /// 矢印キーで行を移動でき (← 折りたたみ/親へ、→ 展開/子へ)、選択は SelectionManager 経由で
    /// SceneView / Inspector と同期する。
    /// ルート一覧の全走査は重いため一定間隔で更新する。
    /// メイドのボーン階層など大量ノードを展開しても重くならないよう、
    /// DrawContent では表示範囲外の行をスキップする
    /// </summary>
    public class HierarchyWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903351;
        private const float RefreshInterval = 0.5f;
        private const float RowHeight = 20f;
        private const float IndentWidth = 14f;
        private const float ToggleWidth = 20f;
        private const float SearchButtonWidth = 44f;
        // 要素どうしの隙間 (縦横共通)
        private const float Spacing = 2f;
        private const float ScrollBarWidth = 16f;
        // この時間内の同一行への再クリックをダブルクリックとみなす
        private const float DoubleClickTime = 0.3f;

        private static SelectionManager selectionManager => SelectionManager.instance;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "Hierarchy";

        /// <summary>表示中の 1 行。矢印キーでの移動もこの並びをたどる</summary>
        private struct Row
        {
            public GameObject go;
            public int depth;
        }

        /// <summary>
        /// 描画用ビュー。行を高さ固定・行番号基準で置くため padding は取らない
        /// (GetDrawRect が padding を加算すると行位置とスクロール量がずれる)
        /// </summary>
        private readonly GUIView _view = new GUIView
        {
            padding = Vector2.zero,
            margin = Spacing,
        };

        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _expanded = new HashSet<int>(); // GetInstanceID
        private float _lastRefreshTime = 0f;
        private string _searchText = "";
        // 選択行を画面内へ送るスクロール量。キー操作の次の描画で反映する
        private int _scrollToRow = -1;
        // 選択変更で表示したいオブジェクト。次の描画で行位置を求めてスクロールする
        private GameObject _pendingReveal = null;
        // ダブルクリック判定用。直前にクリックした行とその時刻
        private GameObject _lastClickedGo = null;
        private float _lastClickTime = 0f;

        private static HierarchyWindow _instance = null;
        public static HierarchyWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HierarchyWindow();
                }
                return _instance;
            }
        }

        private HierarchyWindow()
        {
        }

        public override void Init()
        {
            base.Init();
            selectionManager.onSelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// SceneView / Inspector 等どの経路の選択でも、祖先を展開して行を画面内へ送る。
        /// 行位置は次の描画の BuildRows 後でないと確定しないため、ここでは対象を覚えるだけ
        /// </summary>
        private void OnSelectionChanged(GameObject go)
        {
            _pendingReveal = go;
            if (go == null)
            {
                return;
            }

            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                _expanded.Add(parent.gameObject.GetInstanceID());
            }
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.hierarchyPosX;
            y = config.hierarchyPosY;
            width = config.hierarchyWidth;
            height = config.hierarchyHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.hierarchyPosX = x;
            config.hierarchyPosY = y;
            config.hierarchyWidth = width;
            config.hierarchyHeight = height;
        }

        public override bool savedVisible
        {
            get => config.hierarchyVisible;
            set => config.hierarchyVisible = value;
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                RefreshRoots();
            }
        }

        public override void Update()
        {
            base.Update();

            if (isShowWnd && Time.realtimeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                RefreshRoots();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _roots.Clear();
            _rows.Clear();
            _expanded.Clear();
            _pendingReveal = null;
        }

        /// <summary>ルート GameObject の一覧を取り直す</summary>
        private void RefreshRoots()
        {
            _lastRefreshTime = Time.realtimeSinceStartup;

            _roots.Clear();
            var activeScene = SceneManager.GetActiveScene();
            foreach (var go in activeScene.GetRootGameObjects())
            {
                _roots.Add(go);
            }

            // DontDestroyOnLoad 由来のルートを拾う。専用シーンに属していて IsValid() は true を
            // 返すため、アクティブシーンとの比較で判別する (実機で確認済み)
            foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (transform.parent == null && transform.gameObject.scene != activeScene)
                {
                    _roots.Add(transform.gameObject);
                }
            }
        }

        /// <summary>
        /// 行は GUILayout ではなく固定高で手動配置する。
        /// キー操作でのスクロール量を行番号から正確に計算できるようにするためで、
        /// 併せて表示範囲外の行を描画から省ける
        /// </summary>
        protected override void DrawContent()
        {
            BuildRows();
            ResolvePendingReveal();
            HandleKeyboard();

            _view.Init(ToLocalRect(contentRect));

            // 検索欄 + 手動更新ボタン
            _view.BeginHorizontal();
            {
                var searchWidth = _view.viewRect.width - SearchButtonWidth - Spacing;
                _view.DrawTextField(_searchText, searchWidth, RowHeight, value => _searchText = value);

                if (_view.DrawButton("更新", SearchButtonWidth, RowHeight))
                {
                    RefreshRoots();
                }
            }
            _view.EndLayout();

            // 残りの領域すべてをリストに使う
            var listRect = _view.GetDrawRect(-1, -1);
            if (listRect.height <= 0f)
            {
                return;
            }

            ApplyScrollToRow(listRect.height);

            var contentWidth = Mathf.Max(listRect.width - ScrollBarWidth, 0f);
            var contentHeight = _rows.Count * RowHeight;
            // 内容矩形は毎フレーム行数から与える。EndScrollView が最後に描いた行の位置で
            // 高さを書き戻すが、次フレームのここで上書きされるためスクロール範囲は保たれる。
            // 縦バーは他ウィンドウと揃えて常時表示にする (幅は contentWidth で常に確保済み)
            _view.BeginScrollView(
                listRect.width, listRect.height,
                new Rect(0f, 0f, contentWidth, contentHeight), false, true);
            {
                // 表示範囲に入っている行だけ描く。
                // 行内のボタン操作で _rows が組み直されて縮む場合があるため、毎回件数を見る
                var firstRow = Mathf.Max((int)(_view.scrollPosition.y / RowHeight), 0);
                var lastRow = Mathf.Min(
                    (int)((_view.scrollPosition.y + listRect.height) / RowHeight) + 1, _rows.Count - 1);
                for (var i = firstRow; i <= lastRow && i < _rows.Count; i++)
                {
                    DrawRow(_rows[i], i, contentWidth);
                }
            }
            _view.EndScrollView();
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
                if (_rows[i].go == _pendingReveal)
                {
                    _scrollToRow = i;
                    break;
                }
            }
            _pendingReveal = null;
        }

        /// <summary>展開状態と検索条件から、実際に表示する行を組み立てる</summary>
        private void BuildRows()
        {
            _rows.Clear();
            var searching = !string.IsNullOrEmpty(_searchText);
            foreach (var root in _roots)
            {
                if (root != null)
                {
                    AddRows(root, 0, searching);
                }
            }
        }

        private void AddRows(GameObject go, int depth, bool searching)
        {
            // 検索中は一致するものだけフラット表示
            var matched = !searching ||
                go.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            if (matched)
            {
                _rows.Add(new Row { go = go, depth = searching ? 0 : depth });
            }

            if (searching || _expanded.Contains(go.GetInstanceID()))
            {
                for (var i = 0; i < go.transform.childCount; i++)
                {
                    AddRows(go.transform.GetChild(i).gameObject, depth + 1, searching);
                }
            }
        }

        /// <summary>index 行目を描く。行の位置は行番号から直接決めるため currentPos を毎回置き直す</summary>
        private void DrawRow(Row row, int index, float contentWidth)
        {
            var go = row.go;
            if (go == null)
            {
                return;
            }

            _view.currentPos = new Vector2(row.depth * IndentWidth, index * RowHeight);
            _view.BeginHorizontal();
            {
                if (go.transform.childCount > 0)
                {
                    var isExpanded = _expanded.Contains(go.GetInstanceID());
                    if (_view.DrawButton(isExpanded ? "-" : "+", ToggleWidth, RowHeight))
                    {
                        ToggleExpanded(go);
                    }
                }
                else
                {
                    // 子がなくてもラベルの開始位置は揃える
                    _view.DrawEmpty(ToggleWidth, RowHeight);
                }

                var label = go.activeInHierarchy ? go.name : go.name + " (無効)";
                var labelWidth = contentWidth - _view.currentPos.x;
                var isSelected = selectionManager.selectedObject == go;
                if (_view.DrawButton(
                    label, labelWidth, RowHeight, true,
                    isSelected ? ACCENT_COLOR : Color.white, GUIView.gsLabel))
                {
                    selectionManager.Select(go);
                    OnRowClicked(go);
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// 行クリックのダブルクリック判定。同一行への連続クリックなら SceneView のカメラを
        /// そのオブジェクトへフォーカスさせる。成立時は判定状態を全リセットし、
        /// 成立直後 (0.3 秒以内) の 3 回目のクリックで再フォーカスさせない
        /// </summary>
        private void OnRowClicked(GameObject go)
        {
            var now = Time.realtimeSinceStartup;
            if (go == _lastClickedGo && now - _lastClickTime < DoubleClickTime)
            {
                SceneViewWindow.instance.FocusOn(go);
                _lastClickedGo = null;
                _lastClickTime = 0f;
                return;
            }

            _lastClickedGo = go;
            _lastClickTime = now;
        }

        // ---- キーボード操作 ----

        /// <summary>
        /// 矢印キーで選択行を移動する。
        /// どこかの入力欄が編集中ならキャレット移動を優先して何もしない。
        /// 自窓の検索欄だけでなく Inspector の数値入力も対象にする必要があるため、
        /// コントロール名ではなく「キーボードフォーカスを持つコントロールの有無」で判定する
        /// (GUIView の入力欄はコントロール名を設定しないため名前では判別できない)
        /// </summary>
        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
            {
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    break;
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    break;
                case KeyCode.RightArrow:
                    ExpandOrSelectChild();
                    break;
                case KeyCode.LeftArrow:
                    CollapseOrSelectParent();
                    break;
                default:
                    return;
            }

            e.Use();
        }

        /// <summary>現在の選択が表示行の何番目か。選択なし・非表示なら -1</summary>
        private int GetSelectedRowIndex()
        {
            var selected = selectionManager.selectedObject;
            if (selected == null)
            {
                return -1;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].go == selected)
                {
                    return i;
                }
            }
            return -1;
        }

        private void MoveSelection(int delta)
        {
            if (_rows.Count == 0)
            {
                return;
            }

            var index = GetSelectedRowIndex();
            // 未選択・折りたたまれて見えない場合は端から始める
            var next = index < 0
                ? (delta > 0 ? 0 : _rows.Count - 1)
                : Mathf.Clamp(index + delta, 0, _rows.Count - 1);

            SelectRow(next);
        }

        /// <summary>→: 折りたたまれていれば展開し、展開済みなら最初の子へ移る</summary>
        private void ExpandOrSelectChild()
        {
            var index = GetSelectedRowIndex();
            if (index < 0)
            {
                MoveSelection(1);
                return;
            }

            var go = _rows[index].go;
            if (go.transform.childCount == 0)
            {
                return;
            }

            if (!_expanded.Contains(go.GetInstanceID()))
            {
                _expanded.Add(go.GetInstanceID());
                BuildRows();
                return;
            }

            // 展開済みなら直後の行が最初の子になる
            if (index + 1 < _rows.Count)
            {
                SelectRow(index + 1);
            }
        }

        /// <summary>←: 展開済みなら折りたたみ、そうでなければ親へ移る</summary>
        private void CollapseOrSelectParent()
        {
            var index = GetSelectedRowIndex();
            if (index < 0)
            {
                return;
            }

            var go = _rows[index].go;
            if (_expanded.Remove(go.GetInstanceID()))
            {
                BuildRows();
                return;
            }

            var parent = go.transform.parent;
            if (parent == null)
            {
                return;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                if (_rows[i].go == parent.gameObject)
                {
                    SelectRow(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 展開状態を切り替える。行の描画ループから呼ばれるため、ここでは _rows を組み直さない
        /// (組み直すと反復中のリストが縮んで添字が範囲外になる)。次フレームの BuildRows で反映される
        /// </summary>
        private void ToggleExpanded(GameObject go)
        {
            var id = go.GetInstanceID();
            if (!_expanded.Remove(id))
            {
                _expanded.Add(id);
            }
        }

        private void SelectRow(int index)
        {
            selectionManager.Select(_rows[index].go);
            _scrollToRow = index;
        }

        /// <summary>キー操作で選ばれた行がスクロール範囲外なら、見える位置まで送る</summary>
        private void ApplyScrollToRow(float viewHeight)
        {
            if (_scrollToRow < 0)
            {
                return;
            }

            var top = _scrollToRow * RowHeight;
            var bottom = top + RowHeight;

            var scrollPosition = _view.scrollPosition;
            if (scrollPosition.y > top)
            {
                scrollPosition.y = top;
            }
            else if (scrollPosition.y + viewHeight < bottom)
            {
                scrollPosition.y = bottom - viewHeight;
            }
            _view.scrollPosition = scrollPosition;

            _scrollToRow = -1;
        }
    }
}
