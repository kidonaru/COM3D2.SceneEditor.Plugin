using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーン上の GameObject ツリーを表示するウィンドウ。
    /// 行まわり (展開/折りたたみ・検索・行仮想化・矢印キー移動) は GUITreeView に委譲し、
    /// ここではルート一覧の収集と、SelectionManager 経由の選択同期を担う。
    /// ルート一覧は一定間隔で取り直す。OnChangedSceneLevel ではシーン切替しか拾えず、
    /// シーン内で動的に生成・破棄されるルートはポーリングでしか追えないため
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

        public override void Init()
        {
            base.Init();
            selectionManager.onSelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// SceneView / Inspector 等どの経路の選択でも、祖先を展開して行を画面内へ送る。
        /// 行位置は行構築後でないと確定しないため、ここでは表示予約だけしておく
        /// </summary>
        private void OnSelectionChanged(GameObject go)
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
            _treeView.Clear();
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
        /// 行まわりは GUITreeView に委譲し、
        /// ここでは検索欄・更新ボタンの配置と矢印キー操作の有効化だけを行う
        /// </summary>
        protected override void DrawContent()
        {
            _treeView.HandleKeyboard();

            _view.Init(ToLocalRect(contentRect));

            // 検索欄 + 手動更新ボタン
            _view.BeginHorizontal();
            {
                var searchWidth = _view.viewRect.width - SearchButtonWidth - Spacing;
                _view.DrawTextField(_treeView.searchText, searchWidth, RowHeight,
                    value => _treeView.searchText = value);

                if (_view.DrawButton("更新", SearchButtonWidth, RowHeight))
                {
                    RefreshRoots();
                }
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            // 残りの領域すべてをリストに使う
            _treeView.Draw(_view, _view.GetDrawRect(-1, -1));
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
