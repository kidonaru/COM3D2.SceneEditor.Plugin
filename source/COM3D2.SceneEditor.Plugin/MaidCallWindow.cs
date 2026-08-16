using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ストックメイドの呼出・解除と、呼出済みメイドの配置プリセットを操作するウィンドウ
    /// </summary>
    public class MaidCallWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903353;

        /// <summary>メイド行の高さ。サムネを載せるため通常行より大きくする</summary>
        private static readonly int MAID_ROW_HEIGHT = 40;

        /// <summary>「選択」ボタンの幅。名前ボタンの残り幅計算にも使う</summary>
        private static readonly int SELECT_BUTTON_WIDTH = 50;

        /// <summary>配置モードの選択肢。毎フレーム列挙し直さないようキャッシュする</summary>
        private static readonly List<MaidPlacementPreset.PresetType> _placementModes
            = MTEUtils.GetEnumValues<MaidPlacementPreset.PresetType>();

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "メイド呼出";

        // 対象は一覧の行クリックで選ぶため、選択コンボは出さない
        protected override bool showMaidSelector => false;

        private static MaidCallWindow _instance = null;
        public static MaidCallWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidCallWindow();
                }
                return _instance;
            }
        }

        private MaidCallWindow()
        {
        }

        /// <summary>
        /// 一時選択中のメイド。名前ボタンでトグルし、「呼出」実行時にまとめて反映する
        /// </summary>
        private readonly HashSet<Maid> _selectedMaids = new HashSet<Maid>();

        /// <summary>メイド一覧の名前絞り込みテキスト</summary>
        private string _searchText = "";

        protected override void OnShowChanged(bool visible)
        {
            base.OnShowChanged(visible);
            if (visible)
            {
                SyncSelectionToCalledState();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            base.OnChangedSceneLevel(scene, sceneMode);
            // 遷移先ではメイドの実体が入れ替わるため選択を作り直す
            SyncSelectionToCalledState();
        }

        /// <summary>非表示中も「呼出済み」として扱うため Visible には依存しない</summary>
        private static bool IsMaidCalled(Maid maid)
        {
            return maid != null && maid.body0 != null && maid.body0.isLoadedBody;
        }

        /// <summary>呼出したメイドの中にロード待ちがあるか</summary>
        private bool IsAnyMaidLoading()
        {
            foreach (var maid in maidManager.calledMaids)
            {
                if (maidManager.IsLoading(maid))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 外部（プリセット適用等）からの呼出を一時選択へ反映する。
        /// 未反映だと呼出済みメイドが未選択のまま「呼出」実行で解除されてしまう
        /// </summary>
        public void OnMaidCalled(Maid maid)
        {
            if (maid != null)
            {
                _selectedMaids.Add(maid);
            }
        }

        /// <summary>一時選択を現在の呼出状態から初期化する</summary>
        private void SyncSelectionToCalledState()
        {
            _selectedMaids.Clear();

            // シーン遷移直後は CharacterMgr が未初期化のことがあるため触らない
            if (GameMain.Instance == null || GameMain.Instance.CharacterMgr == null)
            {
                return;
            }

            var stockCount = characterMgr.GetStockMaidCount();
            for (var i = 0; i < stockCount; i++)
            {
                var maid = characterMgr.GetStockMaid(i);
                if (IsMaidCalled(maid))
                {
                    _selectedMaids.Add(maid);
                }
            }
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidCallPosX;
            y = config.maidCallPosY;
            width = config.maidCallWidth;
            height = config.maidCallHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidCallPosX = x;
            config.maidCallPosY = y;
            config.maidCallWidth = width;
            config.maidCallHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidCallVisible;
            set => config.maidCallVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            DrawPlacementRow(view);

            DrawStockMaidList(view);
        }

        /// <summary>配置プリセットの選択行。押すと選択を切り替えつつ即再配置する</summary>
        private void DrawPlacementRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("配置", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var currentMode = (MaidPlacementPreset.PresetType)config.maidPlacementMode;
                foreach (var mode in _placementModes)
                {
                    if (view.DrawButton(mode.ToString(), 60, ROW_HEIGHT, true,
                        mode == currentMode ? EditorSubWindow.ACCENT_COLOR : Color.white))
                    {
                        config.maidPlacementMode = (int)mode;
                        config.dirty = true;
                        HistoryManager.instance.BeforeEdit(null, HistoryScope.Placement,
                            "配置: " + mode);
                        maidManager.ApplyPlacement(mode);
                    }
                }

                // 呼出ボタンは行の右端へ寄せる（ウィンドウが狭い場合は重ねず後続に置く）
                // 案内文はボタンより長いため、状態ごとに右寄せ幅を変える
                var isLoading = IsAnyMaidLoading();
                var showsGuide = _selectedMaids.Count == 0 && maidManager.calledMaids.Count == 0;
                var trailingWidth = showsGuide ? 150f : (isLoading ? 80f : 70f);
                view.currentPos.x = Mathf.Max(view.currentPos.x,
                    view.viewRect.width - view.padding.x * 2 - trailingWidth);

                if (isLoading)
                {
                    // ロード中は呼出ボタンを出さず、ここでロード中表示を兼ねる
                    view.DrawLabel("ロード中...", trailingWidth, ROW_HEIGHT);
                }
                else if (_selectedMaids.Count == 0)
                {
                    if (maidManager.calledMaids.Count > 0)
                    {
                        // 0人選択でも呼出中のメイドが残っていれば全解除できるようにする
                        if (view.DrawButton("解除", trailingWidth, ROW_HEIGHT))
                        {
                            ApplyCallSelection();
                        }
                    }
                    else
                    {
                        // 呼出対象がいないときはボタンの代わりに案内を出す
                        view.DrawLabel("メイドを選択してください", trailingWidth, ROW_HEIGHT, Color.yellow);
                    }
                }
                else if (view.DrawButton($"{_selectedMaids.Count}人呼出", trailingWidth, ROW_HEIGHT))
                {
                    ApplyCallSelection();
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 一時選択を実際の呼出状態へ反映する。
        /// 選択済みの未呼出メイドを呼び出し、選択を外した呼出済みメイドは
        /// このプラグインが呼び出した分だけ解除する
        /// </summary>
        private void ApplyCallSelection()
        {
            var stockCount = characterMgr.GetStockMaidCount();
            for (var i = 0; i < stockCount; i++)
            {
                var maid = characterMgr.GetStockMaid(i);
                if (maid == null)
                {
                    continue;
                }

                var isCalled = IsMaidCalled(maid);
                var isSelected = _selectedMaids.Contains(maid);
                // 自分が呼び出した分だけ解除できる（シーン既存メイドを誤って消さない）
                var canRelease = maidManager.calledMaids.Contains(maid);

                if (isSelected && !isCalled)
                {
                    var calledMaid = maidManager.CallMaid(i);
                    if (calledMaid != null)
                    {
                        // ロード完了後に SceneView を呼び出したメイドへ寄せる
                        maidManager.RequestFocusOnLoaded(calledMaid);
                    }
                }
                else if (!isSelected && isCalled && canRelease)
                {
                    maidManager.ReleaseMaid(maid);
                }
            }
        }

        /// <summary>ストックメイドの一覧と呼出/解除</summary>
        private void DrawStockMaidList(GUIView view)
        {
            view.DrawTextField("検索", LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value => _searchText = value);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            var stockCount = characterMgr.GetStockMaidCount();
            for (var i = 0; i < stockCount; i++)
            {
                var maid = characterMgr.GetStockMaid(i);
                if (maid == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(_searchText) && maid.status.fullNameJpStyle
                    .IndexOf(_searchText, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var isSelected = _selectedMaids.Contains(maid);
                var isTarget = maidManager.targetMaid == maid;
                var isCalled = IsMaidCalled(maid);
                var isLoading = maidManager.IsLoading(maid);

                view.BeginHorizontal();
                {
                    DrawMaidThumb(view, maid);

                    // 名前ボタンは「選択」ボタンを除いた残り幅いっぱいに伸ばす
                    var nameWidth = view.viewRect.width - view.currentPos.x
                        - view.padding.x * 2 - view.margin - SELECT_BUTTON_WIDTH;

                    // 名前ボタンは呼出メイドの一時選択トグル専用。
                    // 「呼出」実行までは実際の呼出状態を変えない
                    if (view.DrawButton(maid.status.fullNameJpStyle, nameWidth, MAID_ROW_HEIGHT, !isLoading,
                        isSelected ? EditorSubWindow.ACCENT_COLOR : Color.white))
                    {
                        ToggleCallSelection(maid, isSelected);
                    }

                    // 「選択」ボタンは操作対象の切替専用。呼出していないメイドは選べない
                    if (view.DrawButton("選択", SELECT_BUTTON_WIDTH, MAID_ROW_HEIGHT, isCalled && !isLoading,
                        isTarget ? EditorSubWindow.ACCENT_COLOR : Color.white))
                    {
                        maidManager.targetMaid = maid;

                        // 選んだメイドへ SceneView も寄せる。
                        // 非表示（退避中）は実座標が遠方にあり、寄せると何もない場所を映すため除く
                        if (maidManager.IsVisible(maid))
                        {
                            SceneViewWindow.instance.FocusOn(maid.gameObject);
                        }
                    }
                }
                view.EndLayout();
            }

            view.EndScrollView();
        }

        /// <summary>
        /// 名前ボタンのクリック処理。呼出メイドの一時選択をトグルする
        /// </summary>
        private void ToggleCallSelection(Maid maid, bool isSelected)
        {
            if (!isSelected)
            {
                _selectedMaids.Add(maid);
                return;
            }

            // 自分が呼び出した分だけ解除できるため、シーン既存メイドは選択を保つ
            if (IsMaidCalled(maid) && !maidManager.calledMaids.Contains(maid))
            {
                return;
            }

            _selectedMaids.Remove(maid);
        }

        /// <summary>メイドのサムネ。半透明の黒boxを下地に敷き、未撮影でも列がずれないようにする</summary>
        private void DrawMaidThumb(GUIView view, Maid maid)
        {
            var savedPos = view.currentPos;
            view.DrawTexture(GUIView.texWhite, MAID_ROW_HEIGHT, MAID_ROW_HEIGHT, new Color(0f, 0f, 0f, 0.5f));

            var thumb = maid.GetThumIcon();
            if (thumb == null)
            {
                return;
            }

            // 下地描画でカーソルが進むため位置を戻して重ねる
            view.currentPos = savedPos;
            view.DrawTexture(thumb, MAID_ROW_HEIGHT, MAID_ROW_HEIGHT);
        }
    }
}
