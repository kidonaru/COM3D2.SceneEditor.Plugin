using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>Hierarchy の表示切り替え。Config に名前で保存される</summary>
    public enum HierarchyViewMode
    {
        PlacedObjects,
        GameObjects,
    }

    /// <summary>
    /// シーンの配置物 (カテゴリ別) または GameObject ツリーを表示するウィンドウ。
    /// 行まわり (展開/折りたたみ・検索・行仮想化・矢印キー移動) は GUITreeView に委譲し、
    /// ここでは一覧の収集と、SelectionManager 経由の選択同期を担う。
    /// 一覧は一定間隔で取り直す。OnChangedSceneLevel ではシーン切替しか拾えず、
    /// シーン内で動的に生成・破棄されるルートはポーリングでしか追えないため。
    /// 配置物ビューは集めた一覧が変わったときだけ木を組み直す
    /// </summary>
    public class HierarchyWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903351;
        private const float RefreshInterval = 0.5f;
        private const float RowHeight = 20f;
        private const float SearchButtonWidth = 44f;
        // 要素どうしの隙間 (縦横共通)
        private const float Spacing = 2f;
        // この時間内の同一行への再クリックをダブルクリックとみなす
        private const float DoubleClickTime = 0.3f;

        private static SelectionManager selectionManager => SelectionManager.instance;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "Hierarchy";

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
        private readonly GUITreeView<GameObject> _treeView = new GUITreeView<GameObject>();

        private const float ModeTabWidth = 80f;
        private static readonly string[] ViewModeLabels = { "配置物", "GameObject" };

        private readonly GUITreeView<PlacedObjectNode> _placedTreeView = new GUITreeView<PlacedObjectNode>();
        private readonly List<PlacedObjectNode> _placedRoots = new List<PlacedObjectNode>();
        // 前回集めた配置物と、次回の収集に使い回すバッファ。比べて違うときだけ木を組み直す
        private List<PlacedObjectSource> _placedSources = new List<PlacedObjectSource>();
        private List<PlacedObjectSource> _collectBuffer = new List<PlacedObjectSource>();
        private PlacedObjectTree _placedTree = PlacedObjectTree.Build(null);
        // 選択中のオブジェクトに対応する行の ID (選択物そのもの、無ければ最も近い配置物の祖先)。無ければ 0
        private int _selectedPlacedId = 0;
        // 選択したが対応する行がまだ無い (配置直後で次の収集前)。組み直した後に展開する
        private bool _placedRevealPending = false;
        // 矢印キーやクリックで選んだカテゴリ見出しの ID。見出しは選ぶ物が無いので SelectionManager とは別に持つ。
        // 持たないと GUITreeView が選択行を見失い、矢印キーで見出しを越えられない。無ければ 0
        private int _selectedCategoryId = 0;

        private static bool isPlacedMode => config.hierarchyViewMode == HierarchyViewMode.PlacedObjects;
        // DontDestroyOnLoad シーンを掴むための番人。SceneManager からは列挙できないため、
        // DontDestroyOnLoad 済みの空 GameObject を 1 つ置いてその scene を借りる
        private static GameObject _dontDestroyOnLoadProbe = null;
        private static Scene _dontDestroyOnLoadScene;
        private float _lastRefreshTime = 0f;
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
            SetupTreeView();
            SetupPlacedTreeView();
        }

        /// <summary>
        /// ツリービューにシーン階層のたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupTreeView()
        {
            // インデント幅等は GUITreeView の既定値をそのまま使う
            _treeView.rowHeight = RowHeight;

            _treeView.getId = go => go.GetInstanceID();
            _treeView.getName = go => go.name;
            _treeView.isAlive = go => go != null;
            _treeView.getChildCount = go => go.transform.childCount;
            _treeView.getChild = (go, i) => go.transform.GetChild(i).gameObject;

            _treeView.getLabel = go => go.activeInHierarchy ? go.name : go.name + " (無効)";
            _treeView.getLabelColor = go =>
                selectionManager.selectedObject == go ? ACCENT_COLOR : Color.white;
            _treeView.isSelected = go => selectionManager.selectedObject == go;
            _treeView.onSelected = go =>
            {
                selectionManager.Select(go);
                OnRowClicked(go);
            };

            _treeView.SetRoots(_roots);
        }

        /// <summary>配置物ツリーのたどり方と行の見た目。選択の扱いは GameObject ビューと同じ</summary>
        private void SetupPlacedTreeView()
        {
            _placedTreeView.rowHeight = RowHeight;

            _placedTreeView.getId = node => node.id;
            _placedTreeView.getName = node => node.label;
            // 見出しは常に出す。配置物は次の収集を待たず、破棄された時点で行から外す
            _placedTreeView.isAlive = node => node.isCategory || node.target != null;
            _placedTreeView.getChildCount = node => node.children.Count;
            _placedTreeView.getChild = (node, i) => node.children[i];

            _placedTreeView.getLabel = node =>
                node.isCategory || node.target.activeInHierarchy ? node.label : node.label + " (無効)";
            _placedTreeView.getLabelColor = node => IsPlacedSelected(node) ? ACCENT_COLOR : Color.white;
            _placedTreeView.isSelected = IsPlacedSelected;
            _placedTreeView.onSelected = node =>
            {
                if (node.isCategory)
                {
                    _selectedCategoryId = node.id;
                    return;
                }
                // 破棄済み (isAlive が次の描画で弾く前) は選ばない
                if (node.target == null)
                {
                    return;
                }
                // 選択中の物へ戻る場合は選択イベントが来ないので、見出しの選択はここで外す
                _selectedCategoryId = 0;
                selectionManager.Select(node.target);
                OnRowClicked(node.target);
            };

            _placedTreeView.SetRoots(_placedRoots);
            ExpandCategories();
        }

        private bool IsPlacedSelected(PlacedObjectNode node)
        {
            if (_selectedCategoryId != 0)
            {
                return node.id == _selectedCategoryId;
            }
            return !node.isCategory && node.id == _selectedPlacedId;
        }

        /// <summary>カテゴリ見出しは最初から開いておく (ID は固定なので木を組む前でも登録できる)</summary>
        private void ExpandCategories()
        {
            foreach (PlacedObjectCategory category in Enum.GetValues(typeof(PlacedObjectCategory)))
            {
                _placedTreeView.Expand(PlacedObjectTree.GetCategoryId(category));
            }
        }

        public override void Init()
        {
            base.Init();
            selectionManager.onSelectionChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged(GameObject go)
        {
            RevealInGameObjectTree(go);
            _selectedCategoryId = 0;

            // 配置直後の物を選んだ場合に行が間に合うよう、今の木に無ければ先に集め直す。
            // 大半は今の木で引けるので、選ぶたびに全配置物を集め直さない
            if (isShowWnd && isPlacedMode && go != null && FindPlacedId(go) == 0)
            {
                RefreshPlacedObjects();
            }
            SyncPlacedSelection(go, true);
        }

        /// <summary>
        /// SceneView / Inspector 等どの経路の選択でも、祖先を展開して行を画面内へ送る。
        /// 行位置は行構築後でないと確定しないため、ここでは表示予約だけしておく
        /// </summary>
        private void RevealInGameObjectTree(GameObject go)
        {
            if (go == null)
            {
                // 選択が外れたら予約も取り消す。残しておくと直前に選ばれていた行へ
                // 意図せずスクロールしてしまう
                _treeView.CancelReveal();
                return;
            }

            _treeView.Reveal(go.GetInstanceID());

            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                _treeView.Expand(parent.gameObject.GetInstanceID());
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
                Refresh();
            }
        }

        public override void Update()
        {
            base.Update();

            if (isShowWnd && Time.realtimeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                Refresh();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _roots.Clear();
            _treeView.Clear();

            _placedSources.Clear();
            _placedTree = PlacedObjectTree.Build(null);
            _placedRoots.Clear();
            _selectedPlacedId = 0;
            _placedRevealPending = false;
            _selectedCategoryId = 0;
            _placedTreeView.Clear();
            // Clear は展開状態も捨てるので見出しを開き直す
            ExpandCategories();
        }

        /// <summary>表示中のビューの一覧だけを取り直す (非表示のビューは切り替えたときに取る)</summary>
        private void Refresh()
        {
            if (isPlacedMode)
            {
                RefreshPlacedObjects();
            }
            else
            {
                RefreshRoots();
            }
        }

        /// <summary>
        /// 配置物を集め直し、前回と違うときだけ木を組み直す。
        /// 変わらない間は木も行も触らないので、ポーリングしても行の組み直しは起きない
        /// </summary>
        private void RefreshPlacedObjects()
        {
            _lastRefreshTime = Time.realtimeSinceStartup;

            _collectBuffer.Clear();
            PlacedObjectCollector.Collect(_collectBuffer);
            if (PlacedObjectTree.SameSources(_placedSources, _collectBuffer))
            {
                return;
            }

            // 今回の一覧を控え、前回の一覧を次回の収集バッファに回す
            var previous = _placedSources;
            _placedSources = _collectBuffer;
            _collectBuffer = previous;

            var previousAncestors = _placedTree.GetAncestorIds(_selectedPlacedId);
            _placedTree = PlacedObjectTree.Build(_placedSources);
            _placedRoots.Clear();
            _placedRoots.AddRange(_placedTree.roots);
            // _placedRoots は同じリストの中身を入れ替えているため、参照比較では検出されない
            _placedTreeView.SetDirty();

            // 新しく出た行が選択中の物かもしれないので対応する行を引き直す。
            // 選択時に行が無かった場合と、選択中の行の親が変わった場合 (メイドへのアタッチ等で
            // 閉じた親の下へ移った) だけ、ここで展開・スクロールする
            var reveal = _placedRevealPending
                || !SameIds(previousAncestors, _placedTree.GetAncestorIds(_selectedPlacedId));
            SyncPlacedSelection(selectionManager.selectedObject, reveal);
        }

        /// <summary>
        /// 選択中のオブジェクトに対応する行を求め、reveal なら祖先を開いて画面内へ送る。
        /// 選択物そのものが一覧に無いときは Transform の祖先で最も近い配置物の行を使う
        /// (GameObject ビューでメッシュの子を選んだ場合など)
        /// </summary>
        private void SyncPlacedSelection(GameObject go, bool reveal)
        {
            _selectedPlacedId = FindPlacedId(go);
            _placedRevealPending = go != null && _selectedPlacedId == 0;

            if (!reveal)
            {
                return;
            }
            if (_selectedPlacedId == 0)
            {
                _placedTreeView.CancelReveal();
                return;
            }

            foreach (var ancestorId in _placedTree.GetAncestorIds(_selectedPlacedId))
            {
                _placedTreeView.Expand(ancestorId);
            }
            _placedTreeView.Reveal(_selectedPlacedId);
        }

        private static bool SameIds(List<int> a, List<int> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private int FindPlacedId(GameObject go)
        {
            for (var t = go != null ? go.transform : null; t != null; t = t.parent)
            {
                var id = t.gameObject.GetInstanceID();
                if (_placedTree.Contains(id))
                {
                    return id;
                }
            }
            return 0;
        }

        /// <summary>
        /// ルート GameObject の一覧を取り直す。
        /// 読み込み済みシーンと DontDestroyOnLoad シーンからルートだけを直接取る。
        /// FindObjectsOfType&lt;Transform&gt;() での全走査より大幅に速いうえ、
        /// FindObjectsOfType が返さない非アクティブなルートも拾える (実機で確認済み)
        /// </summary>
        private void RefreshRoots()
        {
            _lastRefreshTime = Time.realtimeSinceStartup;

            _roots.Clear();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                // 読み込み途中のシーンに GetRootGameObjects を呼ぶと例外になる
                if (scene.IsValid() && scene.isLoaded)
                {
                    AddSceneRoots(scene);
                }
            }

            // DontDestroyOnLoad シーンは SceneManager 管理外で isLoaded の保証がないため、
            // 有効性だけを見る (isLoaded で弾くと DDOL 配下が丸ごと無言で消える)
            var ddolScene = dontDestroyOnLoadScene;
            if (ddolScene.IsValid())
            {
                AddSceneRoots(ddolScene);
            }

            // _roots は同じリストの中身を入れ替えているため、参照比較では検出されない
            _treeView.SetDirty();
        }

        private void AddSceneRoots(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                // 番人自身は一覧に出さない (hideFlags を付けても GetRootGameObjects は返す)
                if (go != _dontDestroyOnLoadProbe)
                {
                    _roots.Add(go);
                }
            }
        }

        private static Scene dontDestroyOnLoadScene
        {
            get
            {
                if (_dontDestroyOnLoadProbe == null)
                {
                    _dontDestroyOnLoadProbe = new GameObject("SceneEditor.HierarchyDdolProbe");
                    _dontDestroyOnLoadProbe.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(_dontDestroyOnLoadProbe);
                    _dontDestroyOnLoadScene = _dontDestroyOnLoadProbe.scene;
                }
                return _dontDestroyOnLoadScene;
            }
        }

        /// <summary>
        /// 行まわりは GUITreeView に委譲し、ここでは表示切り替えタブ・検索欄・更新ボタンの配置と
        /// 矢印キー操作の有効化だけを行う。検索語はビューごとに持つ
        /// </summary>
        protected override void DrawContent()
        {
            if (isPlacedMode)
            {
                _placedTreeView.HandleKeyboard();
            }
            else
            {
                _treeView.HandleKeyboard();
            }

            _view.Init(ToLocalRect(contentRect));

            DrawViewModeTabs();

            // 検索欄 + 手動更新ボタン
            _view.BeginHorizontal();
            {
                var searchWidth = _view.viewRect.width - SearchButtonWidth - Spacing;
                var searchText = isPlacedMode ? _placedTreeView.searchText : _treeView.searchText;
                // 表示を切り替えても入力中の文字が残らないよう、表示ごとに別のコントロールにする
                _view.DrawTextField("", 0f, searchText, searchWidth, RowHeight, value =>
                {
                    if (isPlacedMode)
                    {
                        _placedTreeView.searchText = value;
                    }
                    else
                    {
                        _treeView.searchText = value;
                    }
                }, false, isPlacedMode ? "HierarchyPlacedSearch" : "HierarchyObjectSearch");

                if (_view.DrawButton("更新", SearchButtonWidth, RowHeight))
                {
                    Refresh();
                }
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            // 残りの領域すべてをリストに使う
            var listRect = _view.GetDrawRect(-1, -1);
            if (isPlacedMode)
            {
                _placedTreeView.Draw(_view, listRect);
            }
            else
            {
                _treeView.Draw(_view, listRect);
            }
        }

        /// <summary>配置物 / GameObject の切り替えタブ。切り替えた先の一覧はすぐ取り直して選択行へ送る</summary>
        private void DrawViewModeTabs()
        {
            var current = (int)config.hierarchyViewMode;
            var top = _view.currentPos.y;
            // string[] のままだとジェネリック版 (値の配列から選ぶ) に解決されるため、見出し版を明示する
            var next = _view.DrawTabs((IList<string>)ViewModeLabels, current, ModeTabWidth, RowHeight);
            // DrawTabs は末尾に独自の余白を足すので、通常の 1 行ぶんの行間へ置き直す
            _view.currentPos.y = top + RowHeight + _view.margin;

            if (next == current)
            {
                return;
            }

            config.hierarchyViewMode = (HierarchyViewMode)next;
            config.dirty = true;

            // 非表示だったビューは取り直していないので、ここで取り直す
            Refresh();
            var selected = selectionManager.selectedObject;
            if (isPlacedMode)
            {
                SyncPlacedSelection(selected, true);
            }
            else
            {
                RevealInGameObjectTree(selected);
            }
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
    }
}
