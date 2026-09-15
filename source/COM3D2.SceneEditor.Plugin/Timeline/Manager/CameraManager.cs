using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// MTE の CameraManager から移植。frontCamera (動画の最前面表示) と
    /// LetterBoxView (レターボックス描画) を管理する
    /// </summary>
    public class CameraManager : ManagerBase
    {
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

        private Camera _frontCamera = null;
        private LetterBoxView _letterBoxView = null;

        public Camera mainCamera => PluginUtils.MainCamera;

        public Camera frontCamera
        {
            get
            {
                CreateCamera();
                return _frontCamera;
            }
        }

        /// <summary>
        /// 生成済みの前面カメラ。未生成なら null。
        /// 毎フレームの判定に使うため、frontCamera と違って生成を伴わない
        /// </summary>
        public Camera createdFrontCamera => _frontCamera;

        private CameraManager()
        {
        }

        public override void Init()
        {
        }

        public override void OnLoad()
        {
            CreateCamera();
        }

        /// <summary>
        /// 再有効化で MTEFrontCamera を作り直す。動画の最前面表示と
        /// レターボックスが使う実体で、タイムラインの有無とは無関係に要るため
        /// timeline 必須の OnLoad 任せにはできない
        /// </summary>
        public override void OnPluginEnable()
        {
            CreateCamera();
        }

        public override void OnPluginDisable()
        {
            DestroyCamera();
        }

        /// <summary>レターボックスの描画キャッシュを破棄し、次フレームで再計算させる</summary>
        public void ResetCache()
        {
            if (_letterBoxView != null)
            {
                _letterBoxView.ResetCache();
            }
        }

        private void CreateCamera()
        {
            if (_frontCamera == null)
            {
                GameObject go = new GameObject("MTEFrontCamera");
                _frontCamera = go.AddComponent<Camera>();

                _frontCamera.enabled = true;
                _frontCamera.orthographic = true;
                _frontCamera.orthographicSize = 1.0f;
                _frontCamera.transform.position = new Vector3(0.0f, -6601.0f, -0.4f);
                _frontCamera.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                _frontCamera.fieldOfView = 60f;
                _frontCamera.nearClipPlane = -10f;
                _frontCamera.farClipPlane = 10f;
                _frontCamera.depth = 5f;
                _frontCamera.cullingMask = 256;
                _frontCamera.renderingPath = RenderingPath.Forward;
                _frontCamera.clearFlags = CameraClearFlags.Depth;
                _frontCamera.allowHDR = false;
                _frontCamera.allowMSAA = false;
            }

            if (_letterBoxView == null)
            {
                GameObject go = new GameObject("LetterBoxView");
                _letterBoxView = go.AddComponent<LetterBoxView>();
            }
        }

        private void DestroyCamera()
        {
            if (_frontCamera != null)
            {
                GameObject.Destroy(_frontCamera.gameObject);
                _frontCamera = null;
            }

            if (_letterBoxView != null)
            {
                GameObject.Destroy(_letterBoxView.gameObject);
                _letterBoxView = null;
            }
        }
    }
}
