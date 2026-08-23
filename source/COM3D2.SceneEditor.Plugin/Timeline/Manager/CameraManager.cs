using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// MTE の CameraManager から frontCamera 部分のみ移植。
    /// LetterBoxView (レターボックス描画) は SE スコープ外のため削除している
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

        public Camera mainCamera => PluginUtils.MainCamera;

        public Camera frontCamera
        {
            get
            {
                CreateCamera();
                return _frontCamera;
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
            CreateCamera();
        }

        public override void OnPluginDisable()
        {
            DestroyCamera();
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
        }

        private void DestroyCamera()
        {
            if (_frontCamera != null)
            {
                GameObject.Destroy(_frontCamera.gameObject);
                _frontCamera = null;
            }
        }
    }
}
