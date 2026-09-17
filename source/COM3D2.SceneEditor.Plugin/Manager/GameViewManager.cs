using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
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

        /// <summary>最大化中か。ユーザー操作で選ぶ表示サブモードで config に保存される</summary>
        public bool isMaximized { get; private set; }

        /// <summary>
        /// RTを使わずメインカメラを画面へ直接描画中か。
        /// 最大化中に加え、ウィンドウ一時非表示中 (WindowManager.isWindowsHidden) も
        /// ゲーム画面だけを見たい場面なので直接描画にする。最大化と違い非表示は
        /// isShowWnd や config を書き換えないため、復帰時は元のウィンドウ表示へそのまま戻る
        /// </summary>
        public bool isDirectRender { get; private set; }

        /// <summary>最大化中にNGUIを表示するか。ウィンドウ化に戻すと false へリセットされる</summary>
        public bool isUIVisible { get; private set; }

        public RenderTexture renderTexture { get; private set; }

        /// <summary>メインカメラに載せたギズモ。GameView 上で選択オブジェクトを操作するのに使う</summary>
        public GizmoRenderer gizmoRenderer { get; private set; }
        public BoneLineRenderer boneLineRenderer { get; private set; }
        /// <summary>床グリッド担当。深度でシーンのオブジェクトに隠すためメインカメラに付ける</summary>
        public GridRenderer worldGridRenderer { get; private set; }

        /// <summary>画面分割グリッド担当。ポストエフェクトを避けるため gizmo カメラに付ける</summary>
        public GridRenderer displayGridRenderer { get; private set; }

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
            => instance.isWindowMode && (GameViewWindow.instance.isShowWnd || instance.isDirectRender);

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
            cameraManager.SetClearCameraActive(true, config.backgroundColor);
            HideUICameras(camera);
            camera.targetTexture = renderTexture;
            // RT を付け替えた直後にオーバーレイカメラも揃える (RT 破棄前に参照を外す)
            cameraManager.SyncToMainCamera();
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
            isDirectRender = false;
            isUIVisible = false;
            // 隠したままモードを抜けると、次にモードへ入ったときウィンドウが出てこない
            WindowManager.instance.ResetWindowsHidden();

            // メインカメラが取得できない状況でも、NGUIカメラの復元とリソース解放は必ず行う。
            // ここで打ち切るとUIが消えたまま戻らなくなる
            var camera = mainCamera;
            if (camera != null && camera.targetTexture == renderTexture)
            {
                camera.targetTexture = null;
            }
            DetachGizmoRenderer();
            // RT を付け替えた直後にオーバーレイカメラも揃える (RT 破棄前に参照を外す)
            cameraManager.SyncToMainCamera();
            RestoreUICameras();
            cameraManager.SetClearCameraActive(false, config.backgroundColor);
            ReleaseRenderTexture();
            MTEUtils.Log("エディタウィンドウモードを終了しました");
        }

        /// <summary>
        /// 最大化とウィンドウ化を切り替える。
        /// 描画方式の切替は UpdateDirectRender に任せ、ここでは表示状態と config だけを持つ。
        /// ウィンドウ一時非表示中に呼ばれた場合は直接描画のまま状態だけ変わり、
        /// 復帰時にその状態へ描画方式が揃う
        /// </summary>
        public void SetMaximized(bool maximized)
        {
            if (!isWindowMode || isMaximized == maximized)
            {
                return;
            }

            // 描画方式の切替に失敗すると表示状態だけ先に変わって画面に何も出なくなるため、
            // 切り替えられない状況では状態も変えずに抜ける
            if (mainCamera == null)
            {
                MTEUtils.LogError("メインカメラが取得できないため表示モードを切り替えられません");
                return;
            }

            if (maximized)
            {
                isMaximized = true;
                GameViewWindow.instance.isShowWnd = false;
                // 非表示になるため連結グループからも外す。ウィンドウ化に戻せば元の連結へ復帰
                // させたいので、config の保存済みグループ構成は上書きしない
                WindowConnectManager.instance.OnWindowHidden(GameViewWindow.instance, save: false);
                MTEUtils.Log("GameViewを最大化しました");
            }
            else
            {
                // NGUI表示は最大化中だけの設定なので、フラグを落とす前に戻す
                SetUIVisible(false);
                isMaximized = false;
                GameViewWindow.instance.isShowWnd = true;
                MTEUtils.Log("GameViewをウィンドウ化しました");
            }

            UpdateDirectRender();

            // ExitWindowMode の解除 (モード終了) と違い、ここはユーザー操作・レイアウト適用に
            // よる切替なので、次回の有効化で復元できるよう config へ残す
            config.gameViewMaximized = maximized;
            config.dirty = true;
        }

        /// <summary>
        /// 最大化・ウィンドウ一時非表示の状態から描画方式を揃える。
        /// 直接描画中は RT・クリアカメラを持たないため、関連処理は全て止まる。
        /// メインカメラが取れず切り替えられなかった場合は LateUpdate から再試行される
        /// </summary>
        public void UpdateDirectRender()
        {
            if (!isWindowMode)
            {
                return;
            }

            var directRender = isMaximized || WindowManager.instance.isWindowsHidden;
            if (isDirectRender == directRender)
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

            if (directRender)
            {
                // 他コード箇所 (ExitWindowMode 等) と同じく、自分が設定したRTのときだけ外す
                if (camera.targetTexture == renderTexture)
                {
                    camera.targetTexture = null;
                }
                // RT を付け替えた直後にオーバーレイカメラも揃える (RT 破棄前に参照を外す)
                cameraManager.SyncToMainCamera();
                ReleaseRenderTexture();
                cameraManager.SetClearCameraActive(false, config.backgroundColor);
                isDirectRender = true;
                MTEUtils.Log("GameViewを直接描画に切り替えました");
            }
            else
            {
                CreateRenderTexture(Screen.width, Screen.height);
                cameraManager.SetClearCameraActive(true, config.backgroundColor);
                camera.targetTexture = renderTexture;
                // RT を付け替えた直後にオーバーレイカメラも揃える (RT 破棄前に参照を外す)
                cameraManager.SyncToMainCamera();
                isDirectRender = false;
                MTEUtils.Log("GameViewをRT描画に切り替えました ({0}x{1})", _rtWidth, _rtHeight);
            }
        }

        /// <summary>
        /// config に保存された最大化状態を復元する。
        /// ExitWindowMode で isMaximized は落ちるため、再有効化時に呼び直す必要がある
        /// </summary>
        public void RestoreMaximized()
        {
            if (config.gameViewMaximized)
            {
                SetMaximized(true);
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
        /// ギズモ・骨格線・グリッドの描画を載せる。
        /// メインカメラに載せると OnPostRender の GL 描画にポストエフェクトが乗るため、
        /// 何も映さない専用カメラ (gizmo カメラ) で後から重ね描きし、視点だけメインカメラを使う。
        /// 例外は床グリッドで、シーンのオブジェクトに隠れる必要があり深度が要るため
        /// メインカメラ側に残す (ポストエフェクトは乗る)
        /// </summary>
        private void AttachGizmoRenderer(Camera camera)
        {
            DetachGizmoRenderer();

            var host = cameraManager.gizmoCamera.gameObject;

            gizmoRenderer = host.AddComponent<GizmoRenderer>();
            gizmoRenderer.viewCamera = camera;
            // GameView はゲーム本来の見え方を保ちたいため選択枠は出さない (SceneView のみ)
            gizmoRenderer.showSelectionBounds = false;
            gizmoRenderer.showLightGizmos = false;
            // 編集モード外・ボーン表示 OFF ではギズモも出さない (SceneView はツールバー連動のみ)
            gizmoRenderer.followsBoneVisibility = true;
            gizmoRenderer.isHostActive = IsGizmoHostActive;

            boneLineRenderer = host.AddComponent<BoneLineRenderer>();
            boneLineRenderer.viewCamera = camera;
            boneLineRenderer.isHostActive = IsGizmoHostActive;

            // 床グリッドはメインカメラの深度が要るのでメインカメラ側で描く
            worldGridRenderer = camera.gameObject.AddComponent<GridRenderer>();
            worldGridRenderer.isHostActive = IsGizmoHostActive;

            // 構図合わせ用の画面分割グリッドはゲーム画面側にだけ出す。
            // 深度を使わない画面空間の描画なのでオーバーレイ側へ回せる
            displayGridRenderer = host.AddComponent<GridRenderer>();
            displayGridRenderer.viewCamera = camera;
            displayGridRenderer.isHostActive = IsGizmoHostActive;
            displayGridRenderer.drawWorldGrid = false;
            displayGridRenderer.drawDisplayGrid = true;
        }

        /// <summary>直接描画中は GameView ウィンドウ非表示のままギズモ・骨格線を全画面で生かす</summary>
        private static bool IsGizmoHostActive()
        {
            return GameViewWindow.instance.isShowWnd || instance.isDirectRender;
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

            if (worldGridRenderer != null)
            {
                Object.Destroy(worldGridRenderer);
            }
            worldGridRenderer = null;

            if (displayGridRenderer != null)
            {
                Object.Destroy(displayGridRenderer);
            }
            displayGridRenderer = null;
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
            // 破棄する RT への参照をオーバーレイカメラからも先に外す。
            // CameraManager.LateUpdate はメイド・タイムラインが揃っているときしか走らないため、
            // 次フレームの自動追随はあてにできない
            cameraManager.SyncToMainCamera();
            ReleaseRenderTexture();
            CreateRenderTexture(Screen.width, Screen.height);
            camera.targetTexture = renderTexture;
            cameraManager.SyncToMainCamera();
            MTEUtils.LogDebug("画面サイズの変更に追従しました ({0}x{1})", _rtWidth, _rtHeight);
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

            // 切替時にメインカメラが取れなかった場合の再試行
            UpdateDirectRender();

            if (isDirectRender)
            {
                // 直接描画中はRTを持たないため、サイズ追従も targetTexture の保険も不要。
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
                cameraManager.SyncToMainCamera();
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
                // 自前のオーバーレイカメラ (背景クリア・最前面動画・字幕・ギズモ) は
                // ゲーム UI ではなく編集中も見せる描画物なので隠す対象から外す
                if (cam == null || cam == camera || cameraManager.IsOverlayCamera(cam))
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
