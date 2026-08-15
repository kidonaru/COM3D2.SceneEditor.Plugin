using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// エディタウィンドウモードの実体。
    /// メインカメラをRTへ逃がし、NGUIカメラを隠し、背景をクリアカメラで塗る。
    /// RTは常に画面解像度で作る。ウィンドウのサイズに合わせるとカメラのアスペクト比が
    /// 変わって構図がゲーム本来の見え方からずれるため
    /// </summary>
    public class GameViewManager : ManagerBase
    {
        public bool isWindowMode { get; private set; }

        /// <summary>最大化中か。RTを使わずメインカメラを画面へ直接描画する表示サブモード</summary>
        public bool isMaximized { get; private set; }

        /// <summary>最大化中にNGUIを表示するか。ウィンドウ化に戻すと false へリセットされる</summary>
        public bool isUIVisible { get; private set; }

        public RenderTexture renderTexture { get; private set; }

        /// <summary>メインカメラに載せたギズモ。GameView 上で選択オブジェクトを操作するのに使う</summary>
        public GizmoRenderer gizmoRenderer { get; private set; }
        public BoneLineRenderer boneLineRenderer { get; private set; }
        public GridRenderer gridRenderer { get; private set; }

        private Camera _clearCamera = null;
        private readonly List<Camera> _hiddenUICameras = new List<Camera>();
        private readonly List<UICamera> _disabledUICameraEvents = new List<UICamera>();
        private UICamera _systemUICamera = null;
        private int _systemUIRectFrame = -1;
        private Rect _systemUIRect = new Rect();
        private Camera[] _cameraBuffer = new Camera[0];
        private int _rtWidth = 0;
        private int _rtHeight = 0;

        /// <summary>
        /// メインカメラ。シーン遷移直後など GameMain が未生成・破棄済みの
        /// タイミングがあるため null を返しうる
        /// </summary>
        /// <summary>
        /// GameView がギズモの入力・描画ディスパッチを行える状態か。
        /// window mode でないとメインカメラに GizmoRenderer が付かず、
        /// 非表示かつ非最大化なら描画先が無い。
        /// GizmoHost の稼働判定と GameViewWindow の入力ガードで共有する
        /// </summary>
        public static bool isGizmoDispatchActive
            => instance.isWindowMode && (GameViewWindow.instance.isShowWnd || instance.isMaximized);

        /// <summary>GameView が描画するゲーム本体のカメラ。外部ギズモのディスパッチ先にも使う</summary>
        public static Camera mainCamera
        {
            get
            {
                var gameMain = GameMain.Instance;
                if (gameMain == null || gameMain.MainCamera == null)
                {
                    return null;
                }
                return gameMain.MainCamera.camera;
            }
        }

        private static GameViewManager _instance = null;
        public static GameViewManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameViewManager();
                }
                return _instance;
            }
        }

        private GameViewManager()
        {
        }

        public void EnterWindowMode()
        {
            if (isWindowMode)
            {
                return;
            }

            var camera = mainCamera;
            if (camera == null)
            {
                MTEUtils.LogError("メインカメラが取得できないためモードを開始できません");
                return;
            }

            CreateRenderTexture(Screen.width, Screen.height);
            CreateClearCamera();
            HideUICameras(camera);
            camera.targetTexture = renderTexture;
            AttachGizmoRenderer(camera);
            isWindowMode = true;
            MTEUtils.Log("エディタウィンドウモードを開始しました ({0}x{1})", _rtWidth, _rtHeight);
        }

        public void ExitWindowMode()
        {
            if (!isWindowMode)
            {
                return;
            }
            isWindowMode = false;
            isMaximized = false;
            isUIVisible = false;
            // 隠したままモードを抜けると、次にモードへ入ったときウィンドウが出てこない
            WindowManager.instance.isWindowsHidden = false;

            // メインカメラが取得できない状況でも、NGUIカメラの復元とリソース解放は必ず行う。
            // ここで打ち切るとUIが消えたまま戻らなくなる
            var camera = mainCamera;
            if (camera != null && camera.targetTexture == renderTexture)
            {
                camera.targetTexture = null;
            }
            DetachGizmoRenderer();
            RestoreUICameras();
            DestroyClearCamera();
            ReleaseRenderTexture();
            MTEUtils.Log("エディタウィンドウモードを終了しました");
        }

        /// <summary>
        /// 最大化 (直接描画) とウィンドウ化 (RT描画) を切り替える。
        /// 最大化中は RT・クリアカメラを持たないため、関連処理は全て止まる
        /// </summary>
        public void SetMaximized(bool maximized)
        {
            if (!isWindowMode || isMaximized == maximized)
            {
                return;
            }

            var camera = mainCamera;
            if (camera == null)
            {
                MTEUtils.LogError("メインカメラが取得できないため表示モードを切り替えられません");
                return;
            }

            // ドラッグ途中で座標変換方式が変わると対象が飛ぶため、切替時は必ず打ち切る
            if (gizmoRenderer != null)
            {
                gizmoRenderer.EndDrag();
            }

            if (maximized)
            {
                // 他コード箇所 (ExitWindowMode 等) と同じく、自分が設定したRTのときだけ外す
                if (camera.targetTexture == renderTexture)
                {
                    camera.targetTexture = null;
                }
                ReleaseRenderTexture();
                DestroyClearCamera();
                isMaximized = true;
                GameViewWindow.instance.isShowWnd = false;
                // 非表示になるため連結グループからも外す。最大化は一時的な表示切替なので
                // config の保存済みグループ構成は上書きしない (次回起動時の復元を残す)
                WindowConnectManager.instance.OnWindowHidden(GameViewWindow.instance, save: false);
                MTEUtils.Log("GameViewを最大化しました");
            }
            else
            {
                SetUIVisible(false);
                // 復帰用のバートグルは最大化中しか出ないため、隠したままウィンドウ化すると
                // ウィンドウを戻す手段が無くなる。ウィンドウ化と同時に一時非表示も解除する
                WindowManager.instance.isWindowsHidden = false;
                CreateRenderTexture(Screen.width, Screen.height);
                CreateClearCamera();
                camera.targetTexture = renderTexture;
                isMaximized = false;
                GameViewWindow.instance.isShowWnd = true;
                MTEUtils.Log("GameViewをウィンドウ化しました ({0}x{1})", _rtWidth, _rtHeight);
            }
        }

        /// <summary>最大化中のNGUI表示を切り替える。ウィンドウ化中は常に非表示</summary>
        public void SetUIVisible(bool visible)
        {
            if (!isMaximized || isUIVisible == visible)
            {
                return;
            }

            isUIVisible = visible;
            if (visible)
            {
                RestoreUICameras();
            }
            else
            {
                // 次フレームの LateUpdate を待つと1フレームだけNGUIが残るため即時に隠す
                var camera = mainCamera;
                if (camera != null)
                {
                    HideUICameras(camera);
                }
            }
        }

        /// <summary>
        /// メインカメラへギズモ描画を載せる。ゲーム本体の GameObject を借りるため、
        /// モード終了時には必ず取り外す
        /// </summary>
        private void AttachGizmoRenderer(Camera camera)
        {
            DetachGizmoRenderer();

            gizmoRenderer = camera.gameObject.AddComponent<GizmoRenderer>();
            // GameView はゲーム本来の見え方を保ちたいため選択枠は出さない (SceneView のみ)
            gizmoRenderer.showSelectionBounds = false;
            gizmoRenderer.showLightGizmos = false;
            gizmoRenderer.isHostActive = IsGizmoHostActive;

            boneLineRenderer = camera.gameObject.AddComponent<BoneLineRenderer>();
            boneLineRenderer.isHostActive = IsGizmoHostActive;

            gridRenderer = camera.gameObject.AddComponent<GridRenderer>();
            gridRenderer.isHostActive = IsGizmoHostActive;
            // 構図合わせ用の画面分割グリッドはゲーム画面側にだけ出す
            gridRenderer.drawDisplayGrid = true;
        }

        /// <summary>最大化中は GameView ウィンドウ非表示のままギズモ・骨格線を全画面で生かす</summary>
        private static bool IsGizmoHostActive()
        {
            return GameViewWindow.instance.isShowWnd || instance.isMaximized;
        }

        private void DetachGizmoRenderer()
        {
            if (gizmoRenderer != null)
            {
                Object.Destroy(gizmoRenderer);
            }
            gizmoRenderer = null;

            if (boneLineRenderer != null)
            {
                Object.Destroy(boneLineRenderer);
            }
            boneLineRenderer = null;

            if (gridRenderer != null)
            {
                Object.Destroy(gridRenderer);
            }
            gridRenderer = null;
        }

        /// <summary>
        /// 解像度変更・ウィンドウ/フルスクリーン切替に追従してRTを作り直す
        /// </summary>
        private void UpdateRenderTextureSize(Camera camera)
        {
            if (_rtWidth == Screen.width && _rtHeight == Screen.height)
            {
                return;
            }

            camera.targetTexture = null;
            ReleaseRenderTexture();
            CreateRenderTexture(Screen.width, Screen.height);
            camera.targetTexture = renderTexture;
            MTEUtils.Log("画面サイズの変更に追従しました ({0}x{1})", _rtWidth, _rtHeight);
        }

        public override void LateUpdate()
        {
            if (!isWindowMode)
            {
                return;
            }

            var camera = mainCamera;
            if (camera == null)
            {
                return;
            }

            if (isMaximized)
            {
                // 最大化中はRTを持たないため、サイズ追従も targetTexture の保険も不要。
                // UI表示ONの間は新たに出たUIカメラも隠さない
                if (!isUIVisible)
                {
                    HideUICameras(camera);
                }
                return;
            }

            UpdateRenderTextureSize(camera);

            // タイムライン系 (AMCameraFade 等) に targetTexture を奪われた場合の保険
            if (camera.targetTexture == null)
            {
                camera.targetTexture = renderTexture;
            }

            // モード中に新たに有効化されたUIカメラ (ダイアログ等) も隠す
            HideUICameras(camera);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移時は素の状態へ戻すフェイルセーフ
            if (isWindowMode)
            {
                MTEUtils.Log("シーン遷移のためモードを解除します: {0}", scene.name);
                plugin.isEnable = false;
            }
        }

        public override void OnPluginDisable()
        {
            ExitWindowMode();
        }

        private void CreateRenderTexture(int width, int height)
        {
            _rtWidth = Mathf.Max(width, 64);
            _rtHeight = Mathf.Max(height, 64);
            renderTexture = new RenderTexture(_rtWidth, _rtHeight, 24);
            renderTexture.Create();
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Object.Destroy(renderTexture);
                renderTexture = null;
            }
        }

        private void CreateClearCamera()
        {
            var go = new GameObject("SceneEditorClearCamera");
            Object.DontDestroyOnLoad(go);
            _clearCamera = go.AddComponent<Camera>();
            _clearCamera.depth = -100;
            _clearCamera.clearFlags = CameraClearFlags.SolidColor;
            _clearCamera.backgroundColor = config.backgroundColor;
            _clearCamera.cullingMask = 0;
        }

        private void DestroyClearCamera()
        {
            if (_clearCamera != null)
            {
                Object.Destroy(_clearCamera.gameObject);
                _clearCamera = null;
            }
        }

        /// <summary>
        /// ギアメニュー (SystemShortcut) を描画するNGUIカメラ。
        /// モード中もギアメニューだけは表示・操作可能なままにするため隠す対象から除外する。
        /// シーン遷移で破棄されうるため null になったら取り直す (未取得の間は呼ばれるたびに再試行する)
        /// </summary>
        private UICamera systemUICamera
        {
            get
            {
                if (_systemUICamera == null)
                {
                    var gameMain = GameMain.Instance;
                    var sysShortcut = gameMain != null ? gameMain.SysShortcut : null;
                    if (sysShortcut != null)
                    {
                        // SysShortcut はカメラの子ではないため、同一ルート配下から探す
                        _systemUICamera = sysShortcut.transform.root.GetComponentInChildren<UICamera>();
                    }
                }
                return _systemUICamera;
            }
        }

        /// <summary>
        /// GUI 座標がギアメニュー (SystemShortcut) の表示領域上か。
        /// Input.mousePosition のフック内から毎回呼ばれるため、矩形計算はフレーム単位でキャッシュする
        /// </summary>
        public bool IsOverSystemUI(Vector2 guiPos)
        {
            var frame = Time.frameCount;
            if (_systemUIRectFrame != frame)
            {
                _systemUIRectFrame = frame;
                _systemUIRect = CalcSystemUIRect();
            }
            return _systemUIRect.Contains(guiPos);
        }

        private Rect CalcSystemUIRect()
        {
            var uiCamera = systemUICamera;
            var gameMain = GameMain.Instance;
            var sysShortcut = gameMain != null ? gameMain.SysShortcut : null;
            if (uiCamera == null || sysShortcut == null)
            {
                return new Rect();
            }

            var camera = uiCamera.GetComponent<Camera>();
            if (camera == null)
            {
                return new Rect();
            }

            // AABB の 2 頂点だけの投影で矩形が成立するのは、NGUI の UI カメラが
            // 軸並行な orthographic であることが前提 (実機確認済み)
            var bounds = NGUIMath.CalculateAbsoluteWidgetBounds(sysShortcut.transform);
            var min = camera.WorldToScreenPoint(bounds.min);
            var max = camera.WorldToScreenPoint(bounds.max);
            // スクリーン座標 (左下原点) → GUI座標 (左上原点)
            return Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
        }

        private void HideUICameras(Camera camera)
        {
            var sysUICamera = systemUICamera;

            // 毎フレーム呼ぶため、都度配列を確保する Camera.allCameras は使わない
            var count = Camera.allCamerasCount;
            if (_cameraBuffer.Length < count)
            {
                _cameraBuffer = new Camera[count];
            }
            Camera.GetAllCameras(_cameraBuffer);

            for (var i = 0; i < count; i++)
            {
                var cam = _cameraBuffer[i];
                if (cam == null || cam == camera || cam == _clearCamera)
                {
                    continue;
                }
                if (cam.enabled && (cam.cullingMask & PluginUtils.NGUILayerMask) != 0)
                {
                    // ギアメニューのカメラはモード中も表示・操作可能なままにする
                    if (sysUICamera != null && cam == sysUICamera.GetComponent<Camera>())
                    {
                        continue;
                    }

                    cam.enabled = false;
                    // モード中に外部から enabled を戻されて再度隠した場合の重複追加を防ぐ
                    if (!_hiddenUICameras.Contains(cam))
                    {
                        _hiddenUICameras.Add(cam);
                    }

                    // カメラを止めても UICamera のレイキャストは生きていて、
                    // 見えないボタンが押せてしまうためイベント処理ごと止める
                    var uiCameraEvent = cam.GetComponent<UICamera>();
                    if (uiCameraEvent != null && uiCameraEvent.enabled)
                    {
                        uiCameraEvent.enabled = false;
                        if (!_disabledUICameraEvents.Contains(uiCameraEvent))
                        {
                            _disabledUICameraEvents.Add(uiCameraEvent);
                        }
                    }
                }
            }
        }

        private void RestoreUICameras()
        {
            foreach (var cam in _hiddenUICameras)
            {
                // シーン遷移等で破棄済みのカメラはスキップ (UnityのnullチェックでOK)
                if (cam != null)
                {
                    cam.enabled = true;
                }
            }
            _hiddenUICameras.Clear();

            foreach (var uiCameraEvent in _disabledUICameraEvents)
            {
                if (uiCameraEvent != null)
                {
                    uiCameraEvent.enabled = true;
                }
            }
            _disabledUICameraEvents.Clear();
        }
    }
}
