using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景の一覧表示・切替・削除を行うウィンドウ。
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

        private const string ALL_CATEGORY = "すべて";

        /// <summary>選択中カテゴリ。ALL_CATEGORY なら全カテゴリ表示</summary>
        private string _category = ALL_CATEGORY;
        private string _searchText = "";

        /// <summary>背景モデルの配置管理を開いているか。既定は畳む</summary>
        private bool _isBgModelExpanded = false;

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

            DrawBody();

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

            DrawCurrentBgRow(bgMgr);

            // 背景色と地面は背景の有無に関わらず編集できる
            BackgroundRowDrawer.DrawBgColorRow(_view, ROW_HEIGHT);
            _view.DrawHorizontalLine();
            BackgroundRowDrawer.DrawGroundRows(_view, LABEL_WIDTH, ROW_HEIGHT);

            _view.DrawHorizontalLine();
            DrawFilterRows();
            DrawBgModelSection();
            DrawBgList(bgMgr);
        }

        /// <summary>
        /// 背景モデルの配置管理。旧レイヤー編集ウィンドウ (BGModelTimelineLayerBase.DrawModelManage)
        /// から移設。配置済みのモデルは背景モデルレイヤーのキーと連動する。
        /// 下の背景一覧のスクロールを圧迫しないよう、既定は畳んでおく
        /// </summary>
        private void DrawBgModelSection()
        {
            if (MTEP.TimelineManager.instance.timeline == null)
            {
                return;
            }

            _view.DrawHorizontalLine();

            Action toggle = () => _isBgModelExpanded = !_isBgModelExpanded;
            _view.BeginHorizontal();
            {
                _view.DrawLabel(_isBgModelExpanded ? "▼" : "▶", 20, ROW_HEIGHT,
                    onClickAction: toggle);
                _view.DrawLabel("背景モデルの配置", 150, ROW_HEIGHT, onClickAction: toggle);
            }
            _view.EndLayout();

            if (!_isBgModelExpanded)
            {
                return;
            }

            var bgModelManager = MTEP.BGModelManager.instance;
            var infoList = bgModelManager.modelInfoList;
            if (infoList.Count == 0)
            {
                _view.DrawLabel("背景モデルがありません", -1, ROW_HEIGHT);
                return;
            }

            _view.SetEnabled(_view.focusedComboBox == null
                && MTEP.StudioHackManager.instance.isPoseEditing);

            // 増減は一覧を作り替えるため、描画ループを回し切ってから 1 件だけ反映する
            string addSourceName = null;
            string deleteSourceName = null;

            foreach (var info in infoList)
            {
                var models = bgModelManager.GetModels(info.sourceName);

                _view.BeginHorizontal();
                {
                    var indent = new string(' ', info.depth);
                    var name = indent + "└" + info.displayName;

                    var labelWidth = _view.viewRect.width - _view.currentPos.x - 60 - 10;
                    var labelColor = models.Count > 0 ? Color.green : Color.white;
                    _view.DrawLabel(name, labelWidth, ROW_HEIGHT, labelColor);

                    if (_view.DrawButton("-", 20, ROW_HEIGHT, models.Count > 0))
                    {
                        deleteSourceName = info.sourceName;
                    }

                    _view.DrawLabel(models.Count.ToString(), 20, ROW_HEIGHT);

                    if (_view.DrawButton("+", 20, ROW_HEIGHT))
                    {
                        addSourceName = info.sourceName;
                    }
                }
                _view.EndLayout();
            }

            _view.SetEnabled(_view.focusedComboBox == null);

            if (deleteSourceName != null)
            {
                bgModelManager.DeleteModelBySourceName(deleteSourceName);
            }
            if (addSourceName != null)
            {
                bgModelManager.AddModelBySourceName(addSourceName);
            }
        }

        /// <summary>
        /// 現在背景の操作行。位置・回転の編集は Inspector に寄せたため、
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

            DrawBgTransformRows(bgMgr);
        }

        /// <summary>背景モデルのローカル Transform 編集行。実体は BackgroundRowDrawer 側</summary>
        private void DrawBgTransformRows(BgMgr bgMgr)
        {
            var bgObject = bgMgr.current_bg_object;
            if (bgObject == null)
            {
                return;
            }

            var transform = bgObject.transform;

            _view.DrawHorizontalLine();
            _view.DrawLabel("背景Transform (ローカル)", -1, ROW_HEIGHT);

            BackgroundRowDrawer.DrawBgTransformRows(_view, transform, LABEL_WIDTH, ROW_HEIGHT);
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
