using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// プラグインが作るオーバーレイ系カメラの所有者。
    /// メインカメラのポストエフェクトを避けるため、メインカメラの上に別カメラで重ね描きする
    /// 実体 (背景クリア・最前面動画/レターボックス・字幕・グリッド/ギズモ/骨格線) を
    /// ここで作り、描画先 (GameView の RT) と描画順をメインカメラへ追随させる。
    /// カメラを足すときはこのクラスに足し、撮影側 (ScreenshotManager) は
    /// GetOverlayCameras で列挙するだけにする
    /// </summary>
    public class CameraManager : ManagerBase
    {
        /// <summary>GameView 背景色クリア用。メインより先に画面 (バックバッファ) を塗る</summary>
        public const float ClearCameraDepth = -100f;

        /// <summary>最前面動画・レターボックス用。MTE 由来の固定値</summary>
        public const float FrontCameraDepth = 5f;

        /// <summary>字幕用。PIP (サブカメラ = main + 1) の後に描いて最前面にする</summary>
        public const float TextCameraDepthOffset = 100f;

        /// <summary>グリッド・ギズモ・骨格線用。編集用の線は字幕・最前面動画よりも手前に出す</summary>
        public const float GizmoCameraDepthOffset = 200f;

        /// <summary>
        /// 字幕カメラの配置。SceneView のカメラは "UI" レイヤーも描くため、
        /// シーンから遠く離してキャンバスが編集画面に映り込まないようにする
        /// </summary>
        private static readonly Vector3 TextCameraPosition = new Vector3(0f, -10000f, 0f);

        /// <summary>
        /// 字幕キャンバスをカメラから離す距離。遠いほどキャンバスがワールド座標で大きくなり、
        /// カメラを原点から離して置いても座標精度が落ちにくい
        /// </summary>
        public const float TextCanvasPlaneDistance = 100f;

        /// <summary>
        /// 字幕を載せるレイヤー。シーン側で未使用かつメインカメラのカリング対象外なので、
        /// 字幕カメラだけが描く = ポストエフェクトの影響を受けない。
        /// 字幕カメラの cullingMask と字幕オブジェクトの layer が食い違うと何も映らないため、
        /// TimelineTextManager もここを参照する
        /// </summary>
        public static readonly int TextLayer = LayerMask.NameToLayer("UI");

        private static CameraManager _instance = null;
        public static CameraManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraManager();
                }
                return _instance;
            }
        }

        private Camera _clearCamera = null;
        private Camera _frontCamera = null;
        private Camera _textCamera = null;
        private Camera _gizmoCamera = null;
        private LetterBoxView _letterBoxView = null;

        public Camera mainCamera => PluginUtils.MainCamera;

        /// <summary>背景クリアカメラ。ウィンドウモード外では null</summary>
        public Camera clearCamera => _clearCamera;

        public Camera frontCamera
        {
            get
            {
                CreateFrontCamera();
                return _frontCamera;
            }
        }

        /// <summary>
        /// 生成済みの前面カメラ。未生成なら null。
        /// 毎フレームの判定に使うため、frontCamera と違って生成を伴わない
        /// </summary>
        public Camera createdFrontCamera => _frontCamera;

        /// <summary>字幕カメラ。字幕が初めて要るときに遅延生成する</summary>
        public Camera textCamera
        {
            get
            {
                CreateTextCamera();
                return _textCamera;
            }
        }

        /// <summary>生成済みの字幕カメラ。未生成なら null</summary>
        public Camera createdTextCamera => _textCamera;

        /// <summary>グリッド・ギズモ・骨格線の描画ホスト。何も映さず OnPostRender だけ使う</summary>
        public Camera gizmoCamera
        {
            get
            {
                CreateGizmoCamera();
                return _gizmoCamera;
            }
        }

        private CameraManager()
        {
        }

        public override void Init()
        {
        }

        public override void OnLoad()
        {
            CreateFrontCamera();
        }

        /// <summary>
        /// 再有効化でカメラを作り直す。動画の最前面表示・レターボックス・ギズモが使う実体で、
        /// タイムラインの有無とは無関係に要るため timeline 必須の OnLoad 任せにはできない。
        /// 字幕カメラは字幕生成時の遅延生成に任せる
        /// </summary>
        public override void OnPluginEnable()
        {
            CreateFrontCamera();
            CreateGizmoCamera();
        }

        public override void OnPluginDisable()
        {
            DestroyCameras();
        }

        public override void LateUpdate()
        {
            SyncToMainCamera();
        }

        /// <summary>レターボックスの描画キャッシュを破棄し、次フレームで再計算させる</summary>
        public void ResetCache()
        {
            if (_letterBoxView != null)
            {
                _letterBoxView.ResetCache();
            }
        }

        /// <summary>
        /// 背景クリアカメラの有効/無効を切り替える。
        /// メインカメラが RT へ逃げている間 (ウィンドウ化中) だけ画面を背景色で塗る
        /// </summary>
        public void SetClearCameraActive(bool active, Color backgroundColor)
        {
            if (!active)
            {
                if (_clearCamera != null)
                {
                    Object.Destroy(_clearCamera.gameObject);
                    _clearCamera = null;
                }
                return;
            }

            if (_clearCamera == null)
            {
                var go = new GameObject("SceneEditorClearCamera");
                Object.DontDestroyOnLoad(go);
                _clearCamera = go.AddComponent<Camera>();
                _clearCamera.depth = ClearCameraDepth;
                _clearCamera.clearFlags = CameraClearFlags.SolidColor;
                _clearCamera.cullingMask = 0;
            }
            _clearCamera.backgroundColor = backgroundColor;
        }

        /// <summary>
        /// プラグインのオーバーレイカメラか。GameView の UI カメラ隠蔽から除外する判定に使う
        /// (NGUI レイヤーを描く front カメラがゲーム UI と誤認されるのを防ぐ)
        /// </summary>
        public bool IsOverlayCamera(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }
            return camera == _clearCamera
                || camera == _frontCamera
                || camera == _textCamera
                || camera == _gizmoCamera;
        }

        /// <summary>
        /// メインカメラの後に重ね描きするカメラ (front / text / gizmo) のうち実際に描かれるものを
        /// cameras へ足し、全体を depth 昇順に整える。手動描画の経路 (スクショ・連番出力) が
        /// これらを描かないと撮影結果に写らない。clear カメラは画面側を塗る役なので含めない。
        /// cameras を呼び出し側が渡すのは、毎フレーム呼ぶ連番画像出力がリストを使い回して
        /// アロケーションを避けられるようにするため
        /// </summary>
        public void GetOverlayCameras(List<Camera> cameras)
        {
            AddIfRenderable(cameras, _frontCamera);
            AddIfRenderable(cameras, _textCamera);
            AddIfRenderable(cameras, _gizmoCamera);

            // 画面表示と同じ重なりで合成するため、Unity の描画順と同じ depth 昇順に並べる
            cameras.Sort(CameraDepthComparison);
        }

        private static readonly System.Comparison<Camera> CameraDepthComparison =
            (a, b) => a.depth.CompareTo(b.depth);

        private static void AddIfRenderable(List<Camera> cameras, Camera camera)
        {
            if (camera != null && camera.enabled)
            {
                cameras.Add(camera);
            }
        }

        /// <summary>
        /// 描画先 (GameView の RenderTexture)・描画順・HDR をメインカメラへ追随させる。
        /// LateUpdate で毎フレーム呼ぶほか、GameViewManager がメインカメラの targetTexture を
        /// 付け替えた直後にも呼ぶ (RT を破棄する前に参照を外すため)
        /// </summary>
        public void SyncToMainCamera()
        {
            var main = mainCamera;
            if (main == null)
            {
                return;
            }

            SyncCamera(_frontCamera, main, FrontCameraDepth);
            SyncCamera(_textCamera, main, main.depth + TextCameraDepthOffset);
            SyncCamera(_gizmoCamera, main, main.depth + GizmoCameraDepthOffset);
        }

        private static void SyncCamera(Camera camera, Camera main, float depth)
        {
            if (camera == null)
            {
                return;
            }

            // 破棄済み RT は Unity の演算子オーバーロードで null と等価判定されるため、
            // != 比較だと破棄済み参照の掃除がスキップされる。参照同一性で比較する
            if (!ReferenceEquals(camera.targetTexture, main.targetTexture))
            {
                camera.targetTexture = main.targetTexture;
            }

            if (camera.depth != depth)
            {
                camera.depth = depth;
            }

            // HDR 不一致のまま LDR の GameView RT と往復すると画面に黒点が焼き付くため、
            // ゲーム側の HDR 切替に毎フレーム追随する
            if (camera.allowHDR != main.allowHDR)
            {
                camera.allowHDR = main.allowHDR;
            }
        }

        private void CreateFrontCamera()
        {
            if (_frontCamera == null)
            {
                var go = new GameObject("SceneEditorFrontCamera");
                _frontCamera = go.AddComponent<Camera>();

                _frontCamera.enabled = true;
                _frontCamera.orthographic = true;
                _frontCamera.orthographicSize = 1.0f;
                _frontCamera.transform.position = new Vector3(0.0f, -6601.0f, -0.4f);
                _frontCamera.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                _frontCamera.fieldOfView = 60f;
                _frontCamera.nearClipPlane = -10f;
                _frontCamera.farClipPlane = 10f;
                _frontCamera.depth = FrontCameraDepth;
                _frontCamera.cullingMask = SceneEditor.Plugin.PluginUtils.NGUILayerMask;
                _frontCamera.renderingPath = RenderingPath.Forward;
                _frontCamera.clearFlags = CameraClearFlags.Depth;
                _frontCamera.allowHDR = false;
                _frontCamera.allowMSAA = false;
            }

            if (_letterBoxView == null)
            {
                var go = new GameObject("LetterBoxView");
                _letterBoxView = go.AddComponent<LetterBoxView>();
            }
        }

        private void CreateTextCamera()
        {
            if (_textCamera != null)
            {
                return;
            }

            var go = new GameObject("SceneEditorTextCamera");
            go.transform.position = TextCameraPosition;
            go.transform.rotation = Quaternion.identity;

            _textCamera = go.AddComponent<Camera>();
            _textCamera.cullingMask = 1 << TextLayer;
            // 背景と 3D はメインカメラが描き終えているので、深度だけ消して上に重ねる
            _textCamera.clearFlags = CameraClearFlags.Depth;
            _textCamera.orthographic = false;
            _textCamera.fieldOfView = 60f;
            // 生成直後はメインカメラを取得できないことがあるため既定で切っておく
            // (取得できればこの直後の SyncToMainCamera がメインカメラへ揃え直す)
            _textCamera.allowHDR = false;
            _textCamera.nearClipPlane = 1f;
            // キャンバス (planeDistance の位置) が確実に収まるよう余裕を持たせる
            _textCamera.farClipPlane = TextCanvasPlaneDistance * 2f;

            SyncToMainCamera();
        }

        private void CreateGizmoCamera()
        {
            if (_gizmoCamera != null)
            {
                return;
            }

            var go = new GameObject("SceneEditorGizmoCamera");
            _gizmoCamera = go.AddComponent<Camera>();
            // 何も映さない。グリッド・ギズモ・骨格線は OnPostRender の GL 描画で、
            // 行列は viewCamera (メインカメラ) から取るのでこのカメラの位置・画角は使わない
            _gizmoCamera.cullingMask = 0;
            _gizmoCamera.clearFlags = CameraClearFlags.Depth;
            _gizmoCamera.allowHDR = false;
            _gizmoCamera.allowMSAA = false;

            SyncToMainCamera();
        }

        private void DestroyCameras()
        {
            SetClearCameraActive(false, Color.black);

            if (_frontCamera != null)
            {
                Object.Destroy(_frontCamera.gameObject);
                _frontCamera = null;
            }

            if (_textCamera != null)
            {
                Object.Destroy(_textCamera.gameObject);
                _textCamera = null;
            }

            if (_gizmoCamera != null)
            {
                Object.Destroy(_gizmoCamera.gameObject);
                _gizmoCamera = null;
            }

            if (_letterBoxView != null)
            {
                Object.Destroy(_letterBoxView.gameObject);
                _letterBoxView = null;
            }
        }
    }
}
