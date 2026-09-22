using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景まわりを編集するウィンドウ。
    /// 「背景」タブは一覧表示・切替・削除と背景色、「地面」タブは地面の表示と広さ、
    /// 「モデル」タブは背景モデルの配置数を扱う。
    /// 位置・回転の編集は背景を Inspector で選択して行う。
    /// 背景一覧はフォトモードの PhotoBGData、適用は BgMgr.ChangeBg の同一経路を使う
    /// </summary>
    public class BackgroundWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903366;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "背景";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;
        private static readonly int TAB_WIDTH = 60;

        private const string ALL_CATEGORY = "すべて";

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum BgTabType
        {
            背景,
            地面,
            モデル,
        }

        private BgTabType _tabType = BgTabType.背景;

        /// <summary>モデルタブ内のサブタブ</summary>
        private enum BgModelTabType
        {
            追加,
            管理,
        }

        private BgModelTabType _modelTabType = BgModelTabType.追加;

        private readonly GUITreeView<BGModelNode> _modelTreeView = new GUITreeView<BGModelNode>();
        private List<BGModelNode> _modelTreeRoots = new List<BGModelNode>();
        // ツリーの組み直し判定用。背景の切替とモデル情報の増減で作り直す
        private GameObject _treeBgObject = null;
        private int _treeInfoCount = -1;

        private readonly ItemRowDrawerCache<BGModelManageRowDrawer> _modelManageRowDrawers =
            new ItemRowDrawerCache<BGModelManageRowDrawer>();

        // 制御対象の変更。増減は一覧を作り替えるため、描画ループを回し切ってから
        // 反映する (null なら変更なし)
        private string _pendingCheckSourceName = null;
        private bool _pendingCheckValue = false;

        /// <summary>選択中カテゴリ。ALL_CATEGORY なら全カテゴリ表示</summary>
        private string _category = ALL_CATEGORY;
        private string _searchText = "";

        private readonly GUIComboBox<string> _categoryComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => name,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        /// <summary>カテゴリ一覧のキャッシュ（先頭は ALL_CATEGORY）。毎フレームの再構築を避ける</summary>
        private List<string> _categories = null;

        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private static BackgroundWindow _instance = null;
        public static BackgroundWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BackgroundWindow();
                }
                return _instance;
            }
        }

        private BackgroundWindow()
        {
            SetupModelTreeView();
        }

        /// <summary>
        /// 背景モデルツリーのたどり方と行の見た目を教える。
        /// GUITreeView はゲーム固有の型を知らないため、ここで橋渡しする
        /// </summary>
        private void SetupModelTreeView()
        {
            _modelTreeView.rowHeight = ROW_HEIGHT;

            _modelTreeView.getId = node => node.info.gameObject.GetInstanceID();
            _modelTreeView.getName = node => node.info.displayName;
            _modelTreeView.isAlive = node => node.info.gameObject != null;
            _modelTreeView.getChildCount = node => node.children.Count;
            _modelTreeView.getChild = (node, i) => node.children[i];

            _modelTreeView.getLabel = node => node.info.displayName;
            // 選択中は Hierarchy と同じアクセント色、制御対象は緑で区別する
            _modelTreeView.getLabelColor = node =>
                IsSelected(node) ? ACCENT_COLOR
                    : IsControlled(node) ? Color.green : Color.white;

            // 制御対象かどうかはチェック、ラベルのクリックは Inspector の選択に使う
            _modelTreeView.isSelected = IsSelected;
            _modelTreeView.onSelected = node =>
                SelectionManager.instance.Select(node.info.gameObject);

            _modelTreeView.getChecked = IsControlled;
            _modelTreeView.onCheckChanged = (node, isChecked) =>
            {
                // 反映は描画後 (理由はフィールド宣言のコメント参照)
                _pendingCheckSourceName = node.info.sourceName;
                _pendingCheckValue = isChecked;
            };
        }

        /// <summary>Inspector で選択中のモデルか</summary>
        private static bool IsSelected(BGModelNode node)
        {
            return SelectionManager.instance.selectedObject == node.info.gameObject;
        }

        private static bool IsControlled(BGModelNode node)
        {
            return MTEP.BGModelManager.instance.HasModels(node.info.sourceName);
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.backgroundPosX;
            y = config.backgroundPosY;
            width = config.backgroundWidth;
            height = config.backgroundHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.backgroundPosX = x;
            config.backgroundPosY = y;
            config.backgroundWidth = width;
            config.backgroundHeight = height;
        }

        public override bool savedVisible
        {
            get => config.backgroundVisible;
            set => config.backgroundVisible = value;
        }

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.BGTimelineLayer))
            {
                _tabType = BgTabType.背景;
                return true;
            }
            if (layerType == typeof(MTEP.BGColorTimelineLayer))
            {
                // 背景色は背景タブと地面タブの両方から記録するので、どちらかにいるなら動かさない
                if (_tabType == BgTabType.モデル)
                {
                    _tabType = BgTabType.背景;
                }
                return true;
            }
            if (layerType == typeof(MTEP.BGModelTimelineLayer))
            {
                _tabType = BgTabType.モデル;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 開いたときに一覧を作り直す。マイルームは本プラグイン起動後に
        /// 新規保存されうるため、開くたびに取り直さないと一覧に出てこない
        /// </summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                BackgroundUtils.ReloadBgData();
                _categories = null;
                // 作り直しで消えたカテゴリを選択したままだと一覧が空になる
                if (PhotoBGData.category_list == null ||
                    !PhotoBGData.category_list.ContainsKey(_category))
                {
                    _category = ALL_CATEGORY;
                }
            }
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            try
            {
                DrawBody();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            // (MaidWindowBase と同じ流儀)
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>
        /// 本体の描画。早期 return しても DrawContent 末尾の
        /// ProcessFocus を飛ばさないようメソッドを分けている
        /// </summary>
        private void DrawBody()
        {
            // タブはスクロールビューの外に置き、どこまでスクロールしても切り替えられるようにする
            _tabType = DrawTabHeader(_tabType);

            switch (_tabType)
            {
                case BgTabType.背景:
                    DrawBgTab();
                    break;
                case BgTabType.地面:
                    // 地面は背景色レイヤーが BGGround ごとキー化する
                    TimelineLayerGate.Begin(_view, typeof(MTEP.BGColorTimelineLayer), ROW_HEIGHT);
                    BackgroundRowDrawer.DrawGroundRows(_view, LABEL_WIDTH, ROW_HEIGHT);
                    break;
                case BgTabType.モデル:
                    DrawBgModelTab();
                    break;
            }
        }

        /// <summary>タブ行と、その下の区切り線までをまとめて描く</summary>
        private T DrawTabHeader<T>(T currentTab)
        {
            var nextTab = _view.DrawTabs(currentTab, TAB_WIDTH, ROW_HEIGHT);

            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」になるため、
            // 通常の行間に合わせて詰める (TimelineSettingWindow と同じ流儀)
            _view.currentPos.y -= 5 + GUIView.defaultMargin;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            return nextTab;
        }

        /// <summary>
        /// 背景タブ。現在背景の操作・背景色と、切り替え用の一覧。
        /// 背景の実体を触るのはこのタブだけのため、BgMgr と一覧データの確認もここで行う
        /// </summary>
        private void DrawBgTab()
        {
            var bgMgr = GameMain.Instance != null ? GameMain.Instance.BgMgr : null;
            if (bgMgr == null)
            {
                _view.DrawLabel("BgMgr が見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            if (!BackgroundUtils.EnsureBgDataLoaded())
            {
                _view.DrawLabel("背景一覧を取得できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            // 現在背景の行は背景レイヤー、背景色は背景色レイヤーへ記録されるため区間ごとに判定する。
            // 入れ子にすると内側の End が外側の無効化を解いてしまうので順番に置くこと
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGTimelineLayer), ROW_HEIGHT);
            DrawCurrentBgRow(bgMgr);
            TimelineLayerGate.End(_view);

            // 背景色は背景の有無に関わらず編集できる
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGColorTimelineLayer), ROW_HEIGHT);
            BackgroundRowDrawer.DrawBgColorRow(_view, ROW_HEIGHT);
            TimelineLayerGate.End(_view);

            _view.DrawHorizontalLine();
            // 同じ背景レイヤーの 2 区間目なので注意ラベルと追加ボタンは出さず無効化だけ行う。
            // この区間の End は DrawContent の finally が行う
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGTimelineLayer), ROW_HEIGHT, drawNotice: false);
            DrawFilterRows();
            DrawBgList(bgMgr);
        }

        /// <summary>
        /// モデルタブ。背景モデルの配置管理で、
        /// 配置済みのモデルは背景モデルレイヤーのキーと連動する。
        /// 制御対象にするのは「追加」、対象になったモデルの操作は「管理」で行う
        /// </summary>
        private void DrawBgModelTab()
        {
            // 背景モデルの列挙はタイムライン有効時の LateUpdate でしか同期されないため、
            // タイムライン未読込 (プリセットで復元した直後など) でも一覧が出るようここで同期する。
            // 同期済みなら no-op
            MTEP.BGModelManager.instance.SyncToCurrentBg();

            // End は DrawContent の finally が行う
            TimelineLayerGate.Begin(_view, typeof(MTEP.BGModelTimelineLayer), ROW_HEIGHT);

            _modelTabType = DrawTabHeader(_modelTabType);

            switch (_modelTabType)
            {
                case BgModelTabType.追加:
                    DrawBgModelAddTab();
                    break;
                case BgModelTabType.管理:
                    DrawBgModelManageTab();
                    break;
            }
        }

        /// <summary>
        /// 追加タブ。背景モデルの階層をツリーで出し、チェックで制御対象かどうかだけを変える
        /// </summary>
        private void DrawBgModelAddTab()
        {
            var bgModelManager = MTEP.BGModelManager.instance;
            var infoList = bgModelManager.modelInfoList;
            if (infoList.Count == 0)
            {
                _view.DrawLabel("背景モデルがありません", -1, ROW_HEIGHT);
                return;
            }

            RebuildModelTreeIfNeeded(infoList);
            if (_modelTreeRoots.Count == 0)
            {
                // 情報はあるがツリーに出せるものが無い (複製のみ・実体が消えた等)
                _view.DrawLabel("表示できる背景モデルがありません", -1, ROW_HEIGHT);
                return;
            }

            _view.DrawTextField(_modelTreeView.searchText, -1, ROW_HEIGHT,
                value => _modelTreeView.searchText = value);

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginAutoEditMode();

            _modelTreeView.Draw(_view, _view.GetDrawRect(-1, -1));

            _view.EndAutoEditMode();

            ApplyPendingCheck(bgModelManager);
        }

        /// <summary>
        /// ツリーを組み直す。背景の切替とモデル情報の増減 (複製の追加・削除) で作り直す。
        /// 件数比較で足りるのは 1 フレームに 1 操作しか反映しないためで、
        /// 外部から modelInfoList がまとめて書き換わる経路が増えると成立しなくなる
        /// </summary>
        private void RebuildModelTreeIfNeeded(List<MTEP.BGModelInfo> infoList)
        {
            var bgObject = GameMain.Instance != null && GameMain.Instance.BgMgr != null
                ? GameMain.Instance.BgMgr.BgObject
                : null;

            if (_treeBgObject == bgObject && _treeInfoCount == infoList.Count)
            {
                return;
            }

            _treeBgObject = bgObject;
            _treeInfoCount = infoList.Count;

            _modelTreeRoots = BGModelTree.Build(infoList);
            _modelTreeView.SetRoots(_modelTreeRoots);
            _modelTreeView.SetDirty();
        }

        /// <summary>チェック操作の遅延反映。制御対象から外すときは複製ぶんもまとめて消す</summary>
        private void ApplyPendingCheck(MTEP.BGModelManager bgModelManager)
        {
            var sourceName = _pendingCheckSourceName;
            if (sourceName == null)
            {
                return;
            }
            _pendingCheckSourceName = null;

            if (_pendingCheckValue)
            {
                bgModelManager.AddModelBySourceName(sourceName);
                return;
            }

            // GetModels が返すリストは削除で書き換わるため、先に数えた件数だけ消す
            // (件数を見ながら回すと、消し切れないものがあったとき OnGUI 内で無限ループになる)
            for (var count = bgModelManager.GetModels(sourceName).Count; count > 0; count--)
            {
                bgModelManager.DeleteModelBySourceName(sourceName);
            }
        }

        /// <summary>
        /// 管理タブ。制御対象のモデルを縦に並べ、表示切替・複製・削除と Transform を出す
        /// </summary>
        private void DrawBgModelManageTab()
        {
            var bgModelManager = MTEP.BGModelManager.instance;
            var models = bgModelManager.models;
            if (models.Count == 0)
            {
                _view.DrawLabel("制御対象のモデルがありません", -1, ROW_HEIGHT);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // 増減は一覧を作り替えるため、描画ループを回し切ってから 1 件だけ反映する
            MTEP.BGModelStat actionModel = null;
            var action = BGModelRowAction.None;

            foreach (var model in models)
            {
                var rowAction = _modelManageRowDrawers.Get(model.name).Draw(_view, model);
                if (rowAction != BGModelRowAction.None)
                {
                    actionModel = model;
                    action = rowAction;
                }
            }

            _modelManageRowDrawers.PruneExcept(bgModelManager.modelNames);

            _view.EndScrollView();

            // 開閉は描画要素数を変えるため、行を描き終えてから反映する
            foreach (var model in models)
            {
                _modelManageRowDrawers.Get(model.name).ApplyPendingFold();
            }

            if (actionModel == null)
            {
                return;
            }

            if (action == BGModelRowAction.Duplicate)
            {
                bgModelManager.AddModelBySourceName(actionModel.sourceName);
            }
            else if (action == BGModelRowAction.Delete)
            {
                bgModelManager.DeleteModel(actionModel);
            }
        }

        /// <summary>
        /// 現在背景の操作行。Transform の編集は Inspector に寄せたため、
        /// ここでは削除と選択のみ。背景なし時は案内のみ
        /// </summary>
        private void DrawCurrentBgRow(BgMgr bgMgr)
        {
            if (bgMgr.BgObject == null || bgMgr.Parent == null)
            {
                _view.DrawLabel("背景が表示されていません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("背景を消す", 100, ROW_HEIGHT))
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Background, "背景を消す");
                    bgMgr.DeleteBg();

                    // 消えた背景を Inspector に残さない
                    if (SelectionManager.instance.selectedObject == bgMgr.Parent)
                    {
                        SelectionManager.instance.Select(null);
                    }
                }

                if (_view.DrawButton("背景を選択", 100, ROW_HEIGHT))
                {
                    SelectBg(bgMgr);
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// 背景を Inspector の選択対象にする。SetPos / SetRot やプリセット保存の
        /// 操作対象と同じ親オブジェクト (Parent) を選択して読み書きを一致させる
        /// </summary>
        private static void SelectBg(BgMgr bgMgr)
        {
            if (bgMgr.Parent != null)
            {
                SelectionManager.instance.Select(bgMgr.Parent);
            }
        }

        /// <summary>カテゴリ選択と検索フィルタの行</summary>
        private void DrawFilterRows()
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("カテゴリ", LABEL_WIDTH, ROW_HEIGHT);

                if (_categories == null)
                {
                    _categories = new List<string> { ALL_CATEGORY };
                    _categories.AddRange(PhotoBGData.category_list.Keys);
                }

                _categoryComboBox.items = _categories;
                _categoryComboBox.currentIndex = Mathf.Max(0, _categories.IndexOf(_category));
                _categoryComboBox.onSelected = (name, _) => _category = name;
                _categoryComboBox.DrawButton(_view);
            }
            _view.EndLayout();

            _view.DrawTextField("検索", LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value => _searchText = value);
        }

        /// <summary>フィルタ適用済みの背景ボタン一覧。現在の背景はシアン表示</summary>
        private void DrawBgList(BgMgr bgMgr)
        {
            var currentBgName = bgMgr.GetBGName();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var bgData in PhotoBGData.data)
            {
                if (_category != ALL_CATEGORY && bgData.category != _category)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(_searchText) &&
                    bgData.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var isCurrent = BackgroundUtils.IsCurrentBg(bgData, currentBgName);
                if (_view.DrawButton(bgData.name, -1, ROW_HEIGHT, true,
                    isCurrent ? Color.cyan : Color.white))
                {
                    // ChangeBg は同一背景でも再生成して位置・回転をリセットするため、
                    // 適用済みの背景の再クリックは無視する
                    if (!isCurrent)
                    {
                        HistoryManager.instance.BeforeEdit(null, HistoryScope.Background,
                            "背景変更: " + bgData.name);
                        bgData.Apply();
                        // 配置直後から Inspector で位置・回転を編集できるようにする
                        SelectBg(bgMgr);
                    }
                }
            }

            _view.EndScrollView();
        }
    }
}
