using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 3Dシーンを表示するGameViewウィンドウ。
    /// タイトルバーで移動、4辺+4隅でリサイズ。
    /// RTは画面解像度のまま固定で、表示スケールだけがウィンドウサイズに追従する
    /// </summary>
    public class GameViewWindow
        : IGUIWindow, IResizeCursorProvider, IConnectableWindow, IScreenScalableWindow, ITabVisibleWindow
    {
        public static readonly int WINDOW_ID = 8903349;
        // タイトルとヘッダーボタンが重ならない幅
        public static readonly int MIN_WIDTH = 200;
        public static readonly int MIN_HEIGHT = 240;
        public static readonly int HEADER_HEIGHT = 26;
        public static readonly int FRAME = 4;
        public static readonly int HEADER_BUTTON_HEIGHT = 16;
        public static readonly int HEADER_BUTTON_MARGIN = 2;
        public static readonly int MAXIMIZE_BUTTON_WIDTH = 20;
        public static readonly int LOCK_BUTTON_WIDTH = 20;

        private static Config config => ConfigManager.instance.config;
        private static GameViewManager gameViewManager => GameViewManager.instance;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        /// <summary>ロック中 (移動・リサイズ禁止) か。誤動作防止用で config に永続化する</summary>
        public bool isLocked => config.IsWindowLocked(WINDOW_ID);

        private void ToggleLock()
        {
            config.SetWindowLocked(WINDOW_ID, !isLocked);
            config.dirty = true;
        }

        // IConnectableWindow 実装。タブドッキング非対応のため常に独立ウィンドウ扱い
        public bool isTabVisible => true;
        public TabGroup group => null;
        public int tabWindowId => WINDOW_ID;

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        // RT表示領域 (スクリーンGUI座標、左上原点)
        public Rect viewRect => new Rect(
            _windowRect.x + FRAME,
            _windowRect.y + HEADER_HEIGHT,
            _windowRect.width - FRAME * 2,
            _windowRect.height - HEADER_HEIGHT - FRAME);

        /// <summary>
        /// 実際にRTを描画する矩形 (スクリーンGUI座標)。
        /// 画面と同じアスペクト比を保って viewRect 内に収めるため、
        /// 縦横比が合わない分は余白 (レターボックス) になる。
        /// picking の座標変換もこの矩形を基準にする
        /// </summary>
        public Rect drawRect
        {
            get
            {
                var view = viewRect;
                var screenAspect = (float)Screen.width / Screen.height;

                var width = view.width;
                var height = width / screenAspect;
                if (height > view.height)
                {
                    height = view.height;
                    width = height * screenAspect;
                }

                return new Rect(
                    view.x + (view.width - width) * 0.5f,
                    view.y + (view.height - height) * 0.5f,
                    width,
                    height);
            }
        }

        // 表示領域のピクセルサイズ (config への保存用)
        public int viewPixelWidth => (int)viewRect.width;
        public int viewPixelHeight => (int)viewRect.height;

        private readonly WindowResizeController _resize = new WindowResizeController();

        private static GameViewWindow _instance = null;
        public static GameViewWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameViewWindow();
                }
                return _instance;
            }
        }

        private GameViewWindow()
        {
            // リサイズ中もウィンドウ移動と同じ吸着を効かせる
            _resize.snapper = (rect, edges) => WindowConnectManager.instance.SnapResize(this, rect, edges);
        }

        public void Init()
        {
            RestorePlacement();
        }

        /// <summary>
        /// config の保存済み配置から現在の画面サイズ向けの矩形を組み立てる
        /// (方式の理由は EditorSubWindow.RestorePlacement 参照)
        /// </summary>
        private void RestorePlacement()
        {
            var width = Mathf.Max(config.gameViewWidth + FRAME * 2, MIN_WIDTH);
            var height = Mathf.Max(config.gameViewHeight + HEADER_HEIGHT + FRAME, MIN_HEIGHT);
            var x = config.gameViewPosX >= 0 ? config.gameViewPosX : (Screen.width - width) / 2;
            var y = config.gameViewPosY >= 0 ? config.gameViewPosY : (Screen.height - height) / 2;
            _windowRect = new Rect(x, y, width, height);

            int baseW, baseH;
            if (config.gameViewPosX >= 0 &&
                config.TryGetWindowScreenSize(WINDOW_ID, out baseW, out baseH))
            {
                _windowRect = WindowPlacementScaler.Scale(
                    _windowRect, baseW, baseH, Screen.width, Screen.height, MIN_WIDTH, MIN_HEIGHT);
            }
        }

        public void OnScreenSizeScaled(bool settled)
        {
            // RT は画面解像度固定で後処理が不要なため settled は使わない
            RestorePlacement();
        }

        public void OnGUI()
        {
            if (!isShowWnd)
            {
                return;
            }

            _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawWindow, "GameView", GUIView.gsWin);

            // 画面外へ出ないようクランプ。
            // 連結中はメンバー間のオフセットを壊さないよう個別クランプせず、
            // WindowConnectManager が群のバウンディングボックスでクランプする
            if (!WindowConnectManager.instance.IsConnected(this))
            {
                _windowRect.x = Mathf.Clamp(
                    _windowRect.x, -_windowRect.width + 100, Screen.width - 100);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - HEADER_HEIGHT);
            }
        }

        private void DrawWindow(int id)
        {
            // GameView は画面の大半を占める背景なので、常に最背面へ送る。
            // 手前に来ると SceneView 等のウィンドウを覆い隠してしまう
            GUI.BringWindowToBack(id);

            var rt = gameViewManager.renderTexture;
            if (rt != null)
            {
                // GUI.Window 内はウィンドウ左上原点なので、スクリーン座標の矩形をずらして使う
                var view = ToLocalRect(viewRect);
                var draw = ToLocalRect(drawRect);

                // レターボックスの余白を背景色で塗る
                var prevColor = GUI.color;
                GUI.color = config.backgroundColor;
                GUI.DrawTexture(view, Texture2D.whiteTexture);
                GUI.color = prevColor;

                // アルファ合成なしで描く。ポストエフェクト (被写界深度など) は RT のアルファへ
                // CoC 等の作業値を書き残すため、合成ありだと合焦部分 (アルファ0) が背景色で
                // 塗り潰されてしまう。RT は不透明画像として扱うのが正しい
                GUI.DrawTexture(draw, rt, ScaleMode.StretchToFill, false);
            }

            // 最大化ボタン (ヘッダー右端)。RT 描画をやめて画面へ直接描画するサブモードへ
            // 切り替える。プラグインの終了はメニューバーの「Editor終了」から行う
            var maximizeRect = new Rect(
                _windowRect.width - MAXIMIZE_BUTTON_WIDTH - HEADER_BUTTON_MARGIN * 2,
                (HEADER_HEIGHT - HEADER_BUTTON_HEIGHT) * 0.5f,
                MAXIMIZE_BUTTON_WIDTH,
                HEADER_BUTTON_HEIGHT);
            if (GUI.Button(maximizeRect, "□"))
            {
                gameViewManager.SetMaximized(true);
                return;
            }

            // ロックボタン (最大化ボタンの左隣)。見た目は EditorSubWindow と同じ流儀
            var lockRect = new Rect(
                maximizeRect.x - LOCK_BUTTON_WIDTH - HEADER_BUTTON_MARGIN,
                maximizeRect.y,
                LOCK_BUTTON_WIDTH,
                HEADER_BUTTON_HEIGHT);
            var buttonsLeft = lockRect.x;

            var oldColor = GUI.color;
            // ロック中はアクセントカラーで塗って状態を示す
            GUI.color = isLocked ? EditorSubWindow.ACCENT_COLOR : Color.white;
            if (GUI.Button(lockRect, isLocked ? "◆" : "◇"))
            {
                ToggleLock();
            }
            GUI.color = oldColor;

            // ロック中は移動・リサイズ・スナップ起点の入力を受け付けない (誤動作防止)
            if (isLocked)
            {
                return;
            }

            // モード終了ボタンは先に描いているので、重なる右上角より優先して押せる。
            // 右ドラッグ (カメラ回転) でリサイズが始まらないよう左ボタンに限定する
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 &&
                _resize.TryBegin(_windowRect, e.mousePosition))
            {
                e.Use();
            }

            // ヘッダー上の左押下をスナップ・コネクトのドラッグ起点として通知する
            // (ボタン列の上は除く)。イベントは消費せず GUI.DragWindow の移動に使わせる
            if (e.type == EventType.MouseDown && e.button == 0 &&
                e.mousePosition.y <= HEADER_HEIGHT && e.mousePosition.x < buttonsLeft)
            {
                WindowConnectManager.instance.OnDragMouseDown(this);
            }

            // 吸着中はマネージャーの配置だけに任せる (理由は IsSnapDragging 参照)
            if (!_resize.isResizing &&
                !WindowConnectManager.instance.IsSnapDragging(this))
            {
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, HEADER_HEIGHT));
            }
        }

        /// <summary>
        /// スクリーンGUI座標がリサイズのつかみ範囲上にあるか。
        /// この範囲は描画領域と重なるため、ここでのクリックは3Dシーンへ通さない
        /// </summary>
        public bool IsOverResizeHandle(Vector2 guiPos)
        {
            // ロック中はリサイズ不可なのでつかみ範囲も存在しない扱いにする
            return !isLocked && _resize.IsOverHandle(_windowRect, guiPos);
        }

        // スクリーンGUI座標の矩形を GUI.Window 内のローカル座標へ変換する
        private Rect ToLocalRect(Rect rect)
        {
            return new Rect(rect.x - _windowRect.x, rect.y - _windowRect.y, rect.width, rect.height);
        }

        public bool isResizing => _resize.isResizing;

        public ResizeCursor.Kind desiredCursorKind =>
            _resize.GetCursorKind(
                _windowRect, isShowWnd && gameViewManager.isWindowMode && !isLocked, WINDOW_ID);

        public void Update()
        {
            // RTは画面解像度固定なので、リサイズ時に作り直す必要はない
            if (_resize.UpdateResize(ref _windowRect, MIN_WIDTH, MIN_HEIGHT))
            {
                SavePlacement();
            }

            if (!isResizing)
            {
                UpdateGizmoInput();
            }
        }

        /// <summary>
        /// スクリーンGUI座標をRTピクセル座標へ変換する。
        /// レターボックスの余白を除いた drawRect が基準
        /// </summary>
        public Vector2 GuiToRtPoint(Vector2 guiPos)
        {
            // 最大化中は直接描画のため、画面ピクセル座標がそのままカメラのスクリーン座標。
            // GUI座標 (左上原点) → スクリーン座標 (左下原点) のY反転のみ行う
            if (gameViewManager.isMaximized)
            {
                return new Vector2(guiPos.x, Screen.height - guiPos.y);
            }

            var rt = gameViewManager.renderTexture;
            var rect = drawRect;
            if (rt == null || rect.width <= 0f || rect.height <= 0f)
            {
                return Vector2.zero;
            }

            return new Vector2(
                (guiPos.x - rect.x) * rt.width / rect.width,
                rt.height - (guiPos.y - rect.y) * rt.height / rect.height);
        }

        /// <summary>
        /// 選択オブジェクトのギズモ操作。軸を掴めなかったクリックは何もせず
        /// ゲーム側の操作としてそのまま通す
        /// </summary>
        private void UpdateGizmoInput()
        {
            var gizmo = gameViewManager.gizmoRenderer;
            if (gizmo == null || !GameViewManager.isGizmoDispatchActive)
            {
                return;
            }

            var guiPos = InputRemapper.rawGuiPosition;
            var camera = GameViewManager.mainCamera;

            // ドラッグ継続中は領域外へ出ても維持する
            if (gizmo.isDragging)
            {
                if (Input.GetMouseButton(0))
                {
                    gizmo.UpdateDrag(GuiToRtPoint(guiPos));
                }
                else
                {
                    gizmo.EndDrag();
                }
                return;
            }

            // 外部ギズモのドラッグ継続 (この GameView で始まったものだけ)
            if (GizmoHost.IsExternalDragging(camera))
            {
                if (Input.GetMouseButton(0))
                {
                    GizmoHost.UpdateExternalDrag(camera, GuiToRtPoint(guiPos));
                }
                else
                {
                    GizmoHost.EndExternalDrag();
                }
                return;
            }

            if (Input.GetMouseButtonDown(0) && InputRemapper.IsGameViewActiveAt(guiPos))
            {
                var rtPoint = GuiToRtPoint(guiPos);
                // ギズモ類はハンドルの明示 UI なのでボーン (シーン内容) より優先する
                if (!gizmo.TryBeginDrag(rtPoint) &&
                    !GizmoHost.TryBeginExternalDrag(camera, rtPoint))
                {
                    // ギズモを掴めなかったクリックはボーン選択に回す
                    // (外れた場合は何もせずゲーム側の操作としてそのまま通す)
                    var boneLine = gameViewManager.boneLineRenderer;
                    if (boneLine != null)
                    {
                        boneLine.TryPickBone(rtPoint);
                    }
                }
            }
        }

        public void SavePlacement()
        {
            config.gameViewPosX = (int)_windowRect.x;
            config.gameViewPosY = (int)_windowRect.y;
            config.gameViewWidth = viewPixelWidth;
            config.gameViewHeight = viewPixelHeight;
            config.SetWindowScreenSize(WINDOW_ID, Screen.width, Screen.height);
            config.dirty = true;
        }

        /// <summary>
        /// レイアウト適用用。保存時の画面サイズとの比率でスケールした配置を適用し、
        /// config へも保存する (方式は EditorSubWindow.ApplyPlacement と同じ)
        /// </summary>
        public void ApplyPlacement(
            int x, int y, int viewWidth, int viewHeight,
            int baseScreenWidth, int baseScreenHeight)
        {
            var width = Mathf.Max(viewWidth + FRAME * 2, MIN_WIDTH);
            var height = Mathf.Max(viewHeight + HEADER_HEIGHT + FRAME, MIN_HEIGHT);
            _windowRect = WindowPlacementScaler.Scale(
                new Rect(x, y, width, height),
                baseScreenWidth, baseScreenHeight, Screen.width, Screen.height,
                MIN_WIDTH, MIN_HEIGHT);

            _resize.Cancel();
            SavePlacement();
        }

        public void Close()
        {
            isShowWnd = false;
            _resize.Cancel();
            // モード終了時の片付け。保存済みのグループ構成は次回復元用に残す
            WindowConnectManager.instance.OnWindowHidden(this, save: false);
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
        }
    }
}
