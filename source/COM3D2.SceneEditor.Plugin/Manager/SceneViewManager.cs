using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView 用の専用カメラと RenderTexture のライフサイクル管理。
    /// ウィンドウが閉じている間はカメラを無効化して描画コストをゼロにする。
    /// シーン遷移時は全リソースを破棄し、次回 Activate で作り直す
    /// </summary>
    public class SceneViewManager : ManagerBase
    {
        public Camera sceneCamera { get; private set; }
        public GizmoRenderer gizmoRenderer { get; private set; }
        public BoneLineRenderer boneLineRenderer { get; private set; }
        public GridRenderer gridRenderer { get; private set; }
        public SceneViewCullingFilter cullingFilter { get; private set; }
        public RenderTexture renderTexture { get; private set; }
        public bool isActive { get; private set; }

        /// <summary>
        /// SceneView カメラの近クリップ面。既定 (0.3) だと指や顔へ寄ったときに
        /// 手前が切れてしまうため、注視点の最小距離 (0.1) より十分小さくする
        /// </summary>
        private const float NearClipPlane = 0.01f;

        private GameObject _cameraGo = null;

        private static SceneViewManager _instance = null;
        public static SceneViewManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SceneViewManager();
                }
                return _instance;
            }
        }

        private SceneViewManager()
        {
        }

        public void Activate(int width, int height)
        {
            if (isActive)
            {
                return;
            }

            CreateCamera();
            if (sceneCamera == null)
            {
                return;
            }

            ResizeRenderTexture(width, height);
            sceneCamera.enabled = true;
            // シーン遷移でカメラを作り直した後も保存済みのトグル状態を反映する
            ApplyViewSettings();
            isActive = true;
        }

        public void Deactivate()
        {
            if (!isActive)
            {
                return;
            }
            isActive = false;

            if (sceneCamera != null)
            {
                sceneCamera.enabled = false;
            }
        }

        public void ResizeRenderTexture(int width, int height)
        {
            width = Mathf.Max(width, 64);
            height = Mathf.Max(height, 64);

            if (renderTexture == null ||
                renderTexture.width != width || renderTexture.height != height)
            {
                ReleaseRenderTexture();
                renderTexture = new RenderTexture(width, height, 24);
                renderTexture.name = "SceneEditor SceneView RT";
                renderTexture.Create();
            }

            // カメラを作り直した直後はサイズが同じでも割り当てが外れているため毎回設定する
            if (sceneCamera != null)
            {
                sceneCamera.targetTexture = renderTexture;
            }
        }

        /// <summary>config のトグル状態をカメラ・フィルタ・ギズモへ反映する</summary>
        public void ApplyViewSettings()
        {
            if (sceneCamera == null)
            {
                return;
            }

            sceneCamera.orthographic = config.sceneViewOrthographic;

            if (cullingFilter != null)
            {
                cullingFilter.hideBg = !config.sceneViewShowBg;
                cullingFilter.hideMaid = !config.sceneViewShowMaid;
                cullingFilter.InvalidateCache();
            }
            if (gizmoRenderer != null)
            {
                // 非表示に切り替えた瞬間は、見えないギズモを掴んだままにしない
                if (!config.sceneViewShowGizmo && gizmoRenderer.isDragging)
                {
                    gizmoRenderer.EndDrag();
                }
                gizmoRenderer.drawEnabled = config.sceneViewShowGizmo;
            }
        }

        private void CreateCamera()
        {
            if (_cameraGo != null)
            {
                return;
            }

            _cameraGo = new GameObject("SceneEditor SceneView Camera");
            sceneCamera = _cameraGo.AddComponent<Camera>();
            // NGUI は SceneView に映さない
            sceneCamera.cullingMask = ~PluginUtils.NGUILayerMask;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            sceneCamera.depth = -100; // ゲーム側の描画順に影響させない
            sceneCamera.nearClipPlane = NearClipPlane;
            sceneCamera.enabled = false;

            // 開いた瞬間にゲームと同じ絵が見えるようメインカメラの視点をコピーする
            var gameMain = GameMain.Instance;
            var mainCameraMain = gameMain != null ? gameMain.MainCamera : null;
            var mainCamera = mainCameraMain != null ? mainCameraMain.camera : null;
            if (mainCamera != null)
            {
                _cameraGo.transform.position = mainCamera.transform.position;
                _cameraGo.transform.rotation = mainCamera.transform.rotation;
                sceneCamera.fieldOfView = mainCamera.fieldOfView;
            }

            gizmoRenderer = _cameraGo.AddComponent<GizmoRenderer>();
            boneLineRenderer = _cameraGo.AddComponent<BoneLineRenderer>();
            // 画面分割グリッドは構図合わせ用なので、drawDisplayGrid は既定の false のままにする
            gridRenderer = _cameraGo.AddComponent<GridRenderer>();
            cullingFilter = _cameraGo.AddComponent<SceneViewCullingFilter>();
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture != null)
            {
                if (sceneCamera != null && sceneCamera.targetTexture == renderTexture)
                {
                    sceneCamera.targetTexture = null;
                }
                renderTexture.Release();
                Object.Destroy(renderTexture);
                renderTexture = null;
            }
        }

        public void ReleaseAll()
        {
            isActive = false;
            ReleaseRenderTexture();
            if (_cameraGo != null)
            {
                Object.Destroy(_cameraGo);
            }
            _cameraGo = null;
            sceneCamera = null;
            gizmoRenderer = null;
            boneLineRenderer = null;
            gridRenderer = null;
            cullingFilter = null;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でカメラ GO が破棄される可能性があるため全て作り直す
            ReleaseAll();
        }

        public override void OnPluginDisable()
        {
            ReleaseAll();
        }
    }
}
