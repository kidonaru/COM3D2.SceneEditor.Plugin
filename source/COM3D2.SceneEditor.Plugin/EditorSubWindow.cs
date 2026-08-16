using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView / Hierarchy / Inspector が共有するウィンドウ骨格。
    /// タイトルバーでの移動、リサイズ、閉じるボタン、配置の保存・復元を提供する。
    /// GameViewWindow はレターボックスや「押すとモード自体を終了する」閉じるボタンなど
    /// 固有の事情があるため継承させず、リサイズだけ WindowResizeController で共有する
    /// </summary>
    public abstract class EditorSubWindow
        : IGUIWindow, IResizeCursorProvider, IDockableWindow, IScreenScalableWindow, ITabVisibleWindow
    {
        public static readonly int HEADER_HEIGHT = 26;
        public static readonly int FRAME = 4;
        public static readonly int CLOSE_BUTTON_WIDTH = 20;
        public static readonly int CLOSE_BUTTON_HEIGHT = 16;
        public static readonly int CLOSE_BUTTON_MARGIN = 2;
        public static readonly int LOCK_BUTTON_WIDTH = 20;

        /// <summary>アクティブ状態・ドロップ先の強調に使う共通のアクセント色</summary>
        public static readonly Color ACCENT_COLOR = Color.cyan;

        /// <summary>色相はそのままに不透明度だけ差し替える</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        protected static Config config => ConfigManager.instance.config;
        protected static GameViewManager gameViewManager => GameViewManager.instance;
        protected static WindowManager windowManager => WindowManager.instance;

        protected abstract int windowId { get; }
        protected abstract string windowTitle { get; }
        protected virtual int minWidth => 200;
        protected virtual int minHeight => 160;
        /// <summary>ヘッダーと内容の間に確保する高さ (ツールバー用)</summary>
        protected virtual float contentTopMargin => 0f;

        /// <summary>
        /// コンテンツの空き領域（どのコントロールも押下を消費しなかった場所）の
        /// 左ドラッグでウィンドウ移動を許可するか。コンテンツ全域が
        /// カメラ操作である SceneView は false にする
        /// </summary>
        protected virtual bool allowContentDrag => true;

        public int windowIndex { get; set; }

        /// <summary>所属タブグループ。null なら独立ウィンドウ。TabGroup の Add/Remove だけが設定する</summary>
        public TabGroup group { get; set; }

        /// <summary>
        /// 独立ウィンドウ、またはグループのアクティブタブとして描画対象か。
        /// isShowWnd (表示状態) は含まない点に注意。非表示ウィンドウでも独立なら true になるため、
        /// 描画・入力の判定では必ず isWndVisible を使う
        /// </summary>
        public bool isTabVisible => group == null || group.activeWindow == this;

        /// <summary>
        /// 一時非表示も加味した実効的な表示状態。描画・入力の判定はこちらを使う。
        /// 一時非表示は isShowWnd を書き換えないため、この判定を挟まないと
        /// 消えているウィンドウの領域でリサイズやカメラ操作が反応してしまう
        /// </summary>
        public bool isWndVisible => isShowWnd && isTabVisible && !windowManager.isWindowsHidden;

        /// <summary>ロック中 (移動・リサイズ禁止) か。誤動作防止用で config に永続化する</summary>
        public bool isLocked => config.IsWindowLocked(windowId);

        private void ToggleLock()
        {
            config.SetWindowLocked(windowId, !isLocked);
            config.dirty = true;
        }

        private bool _lastTabVisible = true;

        /// <summary>push されたタブバー状態。null はグループ非加入</summary>
        private string[] _tabTitles;
        private int _tabActiveIndex = -1;

        public void SetTabBarState(string[] titles, int activeIndex)
        {
            _tabTitles = titles;
            _tabActiveIndex = activeIndex;
        }

        /// <summary>
        /// タブの可視状態の変化を検知して OnTabVisibleChanged を発火する。
        /// TabGroup の状態変更点 (SetActive / Add / Remove) から呼ばれる。
        /// 差分がない限り何もしないため、多重に呼んでも安全
        /// </summary>
        public void NotifyTabVisibleChanged()
        {
            if (_lastTabVisible == isTabVisible)
            {
                return;
            }
            _lastTabVisible = isTabVisible;
            OnTabVisibleChanged(_lastTabVisible);
        }

        /// <summary>スクリーンGUI座標のヘッダー矩形。ドロップ判定に使う</summary>
        public Rect headerRect => new Rect(_windowRect.x, _windowRect.y, _windowRect.width, HEADER_HEIGHT);

        /// <summary>タブ表示名 (windowTitle は protected のためグループ描画用に公開する)</summary>
        public string windowTitleForTab => windowTitle;

        /// <summary>保存・復元でグループのメンバーを特定するための ID (windowId をそのまま使う)</summary>
        public int tabWindowId => windowId;

        /// <summary>タブの表示状態 (アクティブ⇔非アクティブ) が変わったときに呼ばれる</summary>
        protected virtual void OnTabVisibleChanged(bool visible)
        {
        }

        private bool _isShowWnd = false;
        public bool isShowWnd
        {
            get => _isShowWnd;
            set
            {
                if (_isShowWnd == value)
                {
                    return;
                }
                _isShowWnd = value;
                OnShowChanged(value);
            }
        }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private readonly WindowResizeController _resize = new WindowResizeController();

        protected EditorSubWindow()
        {
            // リサイズ中もウィンドウ移動と同じ吸着を効かせる
            _resize.snapper = (rect, edges) => WindowConnectManager.instance.SnapResize(this, rect, edges);
        }

        /// <summary>内容の描画領域 (スクリーンGUI座標、左上原点)</summary>
        public Rect contentRect => new Rect(
            _windowRect.x + FRAME,
            _windowRect.y + HEADER_HEIGHT + contentTopMargin,
            _windowRect.width - FRAME * 2,
            _windowRect.height - HEADER_HEIGHT - contentTopMargin - FRAME);

        // 内容領域のピクセルサイズ (config への保存用)
        public int contentPixelWidth => (int)contentRect.width;
        public int contentPixelHeight => (int)contentRect.height;

        /// <summary>config から配置を読む。座標が負なら未初期化 (画面中央へ配置する)</summary>
        protected abstract void LoadPlacement(out int x, out int y, out int width, out int height);

        /// <summary>現在の配置を config へ書く</summary>
        protected abstract void StorePlacement(int x, int y, int width, int height);

        /// <summary>config に保存された表示状態。モード開始時の復元に使う</summary>
        public abstract bool savedVisible { get; set; }

        /// <summary>ヘッダー下の内容を描画する。座標はウィンドウローカル (ToLocalRect を使う)</summary>
        protected abstract void DrawContent();

        /// <summary>ツールバー等、内容より上に描く要素。contentTopMargin を確保した窓だけ使う</summary>
        protected virtual void DrawToolbar()
        {
        }

        public virtual void Init()
        {
            RestorePlacement();
        }

        /// <summary>
        /// config の保存済み配置から現在の画面サイズ向けの矩形を組み立てる。
        /// 保存時と画面サイズが異なる場合は保存時の比率を保つようスケールする。
        /// 常に保存値からの再計算にすることで、最小サイズクランプ後に
        /// 画面を戻したときも元の比率へ正確に復元できる
        /// </summary>
        private void RestorePlacement()
        {
            int x, y, contentWidth, contentHeight;
            LoadPlacement(out x, out y, out contentWidth, out contentHeight);

            var width = Mathf.Max(contentWidth + FRAME * 2, minWidth);
            var height = Mathf.Max(
                contentHeight + HEADER_HEIGHT + (int)contentTopMargin + FRAME, minHeight);
            _windowRect = new Rect(
                x >= 0 ? x : (Screen.width - width) / 2,
                y >= 0 ? y : (Screen.height - height) / 2,
                width,
                height);

            // x >= 0 は保存済み配置がある印。未保存の中央寄せ配置はスケール不要
            int baseW, baseH;
            if (x >= 0 && config.TryGetWindowScreenSize(windowId, out baseW, out baseH))
            {
                _windowRect = WindowPlacementScaler.Scale(
                    _windowRect, baseW, baseH, Screen.width, Screen.height, minWidth, minHeight);
            }
        }

        public void OnScreenSizeScaled(bool settled)
        {
            // リサイズハンドルのドラッグ追跡が残っていると、次フレームの UpdateResize が
            // 開始時の基準矩形で上書きして再計算を打ち消すためキャンセルする
            _resize.Cancel();

            // config は更新しない (再計算方式の理由は RestorePlacement 参照)
            RestorePlacement();

            // SceneView 等の描画バッファの作り直しはリサイズ確定と同じ後処理で行う。
            // 毎フレームの作り直しは重いため、サイズが安定した 1 回だけにする
            if (settled)
            {
                OnResizeEnd();
            }
        }

        public virtual void OnGUI()
        {
            if (!isWndVisible)
            {
                return;
            }

            // グループ時はタブバーを自前描画するのでタイトルは空にする (push された状態で判定する)
            var title = _tabTitles != null ? "" : windowTitle;
            _windowRect = GUI.Window(windowId, _windowRect, DrawWindow, title, GUIView.gsWin);

            // 画面外へ出ないようクランプ。
            // 連結中はメンバー間のオフセットを壊さないよう個別クランプせず、
            // WindowConnectManager が群のバウンディングボックスでクランプする
            if (!WindowConnectManager.instance.IsConnected(this))
            {
                _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 100, Screen.width - 100);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - HEADER_HEIGHT);
            }

            if (group != null)
            {
                group.SyncRect(_windowRect);
            }
        }

        private void DrawWindow(int id)
        {
            DrawContent();
            DrawToolbar();

            if (_tabTitles != null)
            {
                DrawTabBar();
            }

            // 閉じるボタン (ヘッダー右端)。ヘッダー高さを変えても縦中央に来るようにする
            var closeRect = new Rect(
                _windowRect.width - CLOSE_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN * 2,
                (HEADER_HEIGHT - CLOSE_BUTTON_HEIGHT) * 0.5f,
                CLOSE_BUTTON_WIDTH,
                CLOSE_BUTTON_HEIGHT);
            if (!DrawHeaderButtons(closeRect))
            {
                // 閉じられたウィンドウには以降の入力判定を走らせない
                return;
            }

            DrawDropHighlight();

            // ロック中は移動・リサイズ・ドッキング起点の入力を受け付けない (誤動作防止)
            if (!isLocked)
            {
                HandleDragInput(closeRect);
            }
        }

        /// <summary>
        /// ヘッダー右のボタン列を描く。閉じるボタンが押されたら false を返す。
        /// 閉じるボタンを先に描くことで、重なるリサイズ角より優先して押せる
        /// </summary>
        private bool DrawHeaderButtons(Rect closeRect)
        {
            // グループ時はアクティブタブだけを閉じる
            if (GUI.Button(closeRect, "x"))
            {
                isShowWnd = false;
                TabGroupManager.instance.RemoveFromGroup(this);
                WindowConnectManager.instance.OnWindowHidden(this);
                return false;
            }

            // ロックボタン (閉じるボタンの左隣)
            var lockRect = new Rect(
                closeRect.x - LOCK_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN,
                closeRect.y,
                LOCK_BUTTON_WIDTH,
                CLOSE_BUTTON_HEIGHT);
            var oldColor = GUI.color;
            // ロック中はアクセントカラーで塗って状態を示す
            GUI.color = isLocked ? ACCENT_COLOR : Color.white;
            if (GUI.Button(lockRect, isLocked ? "◆" : "◇"))
            {
                ToggleLock();
            }
            GUI.color = oldColor;

            return true;
        }

        /// <summary>
        /// ドロップ先ハイライト。GUI.Window は通常の GUI より手前に描かれるため、
        /// ウィンドウ外から被せることができず、各ウィンドウが自分のヘッダーへ描く
        /// </summary>
        private void DrawDropHighlight()
        {
            if (!TabGroupManager.instance.IsDropTarget(this))
            {
                return;
            }

            var oldColor = GUI.color;
            GUI.color = WithAlpha(ACCENT_COLOR, 0.4f);
            GUI.DrawTexture(new Rect(0, 0, _windowRect.width, HEADER_HEIGHT), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        /// <summary>リサイズ開始判定とウィンドウ移動ドラッグを処理する</summary>
        private void HandleDragInput(Rect closeRect)
        {
            // 右ドラッグ (視点回転) や中ドラッグ (パン) でリサイズが始まらないよう左ボタンに限定する
            // 判定のたびに e.type を読み直す。リサイズ開始やコントロールが
            // e.Use() で押下を消費した後は MouseDown ではなくなり、後続が空振りするのが正しい
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 &&
                _resize.TryBegin(_windowRect, e.mousePosition))
            {
                e.Use();
            }

            // ヘッダー上の左押下をドッキング判定の起点として通知する。
            // イベントは消費せず、そのまま GUI.DragWindow の移動に使わせる
            if (e.type == EventType.MouseDown && e.button == 0 &&
                e.mousePosition.y <= HEADER_HEIGHT && !closeRect.Contains(e.mousePosition))
            {
                NotifyDragMouseDown();
            }

            if (_resize.isResizing)
            {
                return;
            }

            // コントロールが押下を処理すると e.Use() で消費されるため、
            // この時点で未消費の MouseDown は「空き領域」への押下。
            // 空き領域ドラッグもヘッダードラッグと同じくドッキング判定の起点にする
            if (allowContentDrag &&
                e.type == EventType.MouseDown && e.button == 0 &&
                e.mousePosition.y > HEADER_HEIGHT)
            {
                NotifyDragMouseDown();
            }

            // 吸着中はマネージャーの配置だけに任せる (理由は IsSnapDragging 参照)
            if (WindowConnectManager.instance.IsSnapDragging(this))
            {
                return;
            }

            var dragRect = allowContentDrag
                ? new Rect(0, 0, _windowRect.width, _windowRect.height)
                : new Rect(0, 0, _windowRect.width, HEADER_HEIGHT);
            GUI.DragWindow(dragRect);
        }

        /// <summary>移動ドラッグの起点を各マネージャーへ通知する</summary>
        private void NotifyDragMouseDown()
        {
            TabGroupManager.instance.OnHeaderMouseDown(this);
            WindowConnectManager.instance.OnDragMouseDown(this);
        }

        /// <summary>グループ時のタブ列。描画は MTEUtils の TabBarDrawer と共通</summary>
        private void DrawTabBar()
        {
            // タブ列がヘッダー右のボタン (閉じる + ロック) へ食い込まないよう、利用可能幅を先に確定する
            var available = _windowRect.width - FRAME * 2
                - (CLOSE_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN * 2)
                - (LOCK_BUTTON_WIDTH + CLOSE_BUTTON_MARGIN);

            TabBarDrawer.Draw(
                _tabTitles, _tabActiveIndex,
                FRAME, (HEADER_HEIGHT - TabBarDrawer.TAB_HEIGHT) * 0.5f, available,
                (index, pos) => TabGroupManager.instance.OnTabPressed(this, index, pos));
        }

        /// <summary>スクリーンGUI座標がリサイズのつかみ範囲上にあるか</summary>
        public bool IsOverResizeHandle(Vector2 guiPos)
        {
            // ロック中はリサイズ不可なのでつかみ範囲も存在しない扱いにする
            return !isLocked && _resize.IsOverHandle(_windowRect, guiPos);
        }

        /// <summary>スクリーンGUI座標の矩形を GUI.Window 内のローカル座標へ変換する</summary>
        protected Rect ToLocalRect(Rect rect)
        {
            return new Rect(rect.x - _windowRect.x, rect.y - _windowRect.y, rect.width, rect.height);
        }

        public bool isResizing => _resize.isResizing;

        public ResizeCursor.Kind desiredCursorKind =>
            _resize.GetCursorKind(
                _windowRect, isWndVisible && gameViewManager.isWindowMode && !isLocked, windowId);

        public virtual void Update()
        {
            if (_resize.UpdateResize(ref _windowRect, minWidth, minHeight))
            {
                OnResizeEnd();
                SavePlacement();
            }
        }

        /// <summary>
        /// リサイズ確定時に呼ばれる。ドラッグ中に毎フレーム RT を作り直すのは重いため、
        /// 描画バッファのサイズ追従はここで行う
        /// </summary>
        protected virtual void OnResizeEnd()
        {
        }

        /// <summary>表示状態が変わったときに呼ばれる</summary>
        protected virtual void OnShowChanged(bool visible)
        {
        }

        public void SavePlacement()
        {
            StorePlacement((int)_windowRect.x, (int)_windowRect.y, contentPixelWidth, contentPixelHeight);
            config.SetWindowScreenSize(windowId, Screen.width, Screen.height);
            savedVisible = _isShowWnd;
            config.dirty = true;
        }

        /// <summary>
        /// レイアウト適用用。保存時の画面サイズとの比率でスケールした配置を適用し、
        /// config へも保存する。矩形の組み立ては RestorePlacement と同じ方式
        /// </summary>
        public void ApplyPlacement(
            int x, int y, int contentWidth, int contentHeight,
            int baseScreenWidth, int baseScreenHeight)
        {
            var width = Mathf.Max(contentWidth + FRAME * 2, minWidth);
            var height = Mathf.Max(
                contentHeight + HEADER_HEIGHT + (int)contentTopMargin + FRAME, minHeight);
            _windowRect = WindowPlacementScaler.Scale(
                new Rect(x, y, width, height),
                baseScreenWidth, baseScreenHeight, Screen.width, Screen.height,
                minWidth, minHeight);

            // リサイズドラッグの追跡が残っていると次フレームで上書きされるため中断する
            _resize.Cancel();
            // SceneView 等の描画バッファをサイズへ追従させる
            OnResizeEnd();
            SavePlacement();
        }

        public virtual void Close()
        {
            isShowWnd = false;
            _resize.Cancel();
            // モード終了時の片付け。保存済みのグループ構成は次回復元用に残す
            TabGroupManager.instance.RemoveFromGroup(this, save: false);
            WindowConnectManager.instance.OnWindowHidden(this, save: false);
        }

        public virtual void OnLoad()
        {
        }

        public virtual void OnScreenSizeChanged()
        {
        }

        public virtual void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
        }
    }
}
