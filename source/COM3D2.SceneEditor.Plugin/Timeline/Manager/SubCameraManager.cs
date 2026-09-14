using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// サブカメラをメイドへ追従させる。設定と追従点の算出は MaidFollowState に委譲し、
    /// ここでは LateUpdate でカメラ Transform へ書き込むだけを担う
    /// </summary>
    public class MaidFollowSubCamera : MonoBehaviour
    {
        public Transform targetTransform;
        public readonly MaidFollowState state = new MaidFollowState();

        public int maidSlotNo
        {
            get => state.maidSlotNo;
            set => state.maidSlotNo = value;
        }

        public MaidPointType maidPointType
        {
            get => state.maidPointType;
            set => state.maidPointType = value;
        }

        public bool followRotation
        {
            get => state.followRotation;
            set => state.followRotation = value;
        }

        public Vector3 offset
        {
            get => state.offset;
            set => state.offset = value;
        }

        public Vector3 eulerAnglesOffset
        {
            get => state.eulerAnglesOffset;
            set => state.eulerAnglesOffset = value;
        }

        public MaidCache maidCache => state.maidCache;
        public Maid maid => state.maid;
        public bool isFollow => state.isFollow;

        private static SceneEditorHack studioHack => SceneEditorHack.instance;

        private void LateUpdate()
        {
            if (studioHack == null || targetTransform == null)
            {
                return;
            }

            Vector3 anchor;
            Quaternion faceRotation;
            if (!state.TryGetAnchor(out anchor, out faceRotation))
            {
                return;
            }

            targetTransform.position = state.GetFollowPosition(anchor, faceRotation);
            if (state.followRotation)
            {
                targetTransform.rotation = faceRotation * Quaternion.Euler(state.eulerAnglesOffset);
            }
        }
    }

    public class SubCameraData
    {
        public string name;
        public string displayName;
        public Camera camera;
        public Rect viewportRect;

        private MaidFollowSubCamera _follow = null;
        public MaidFollowSubCamera follow
        {
            get
            {
                if (_follow == null)
                {
                    _follow = camera.gameObject.GetOrAddComponent<MaidFollowSubCamera>();
                }
                _follow.targetTransform = camera.transform;
                return _follow;
            }
        }

        public bool visible
        {
            get => camera != null && camera.enabled;
            set
            {
                if (camera != null)
                {
                    camera.enabled = value;
                }
            }
        }

        // 追従中はオフセット、非追従中はワールド座標として扱う
        public Vector3 position
        {
            get
            {
                if (follow.isFollow)
                {
                    return follow.offset;
                }
                return camera.transform.position;
            }
            set
            {
                if (follow.isFollow)
                {
                    follow.offset = value;
                }
                else
                {
                    camera.transform.position = value;
                }
            }
        }

        // 向き反映中はオフセット、それ以外はワールド回転として扱う
        // (位置は常に追従点基準になるのに対し、回転は向き反映時のみ追従点基準になるため
        //  position とは条件が非対称になる)
        public Quaternion rotation
        {
            get
            {
                if (follow.isFollow && follow.followRotation)
                {
                    return Quaternion.Euler(follow.eulerAnglesOffset);
                }
                return camera.transform.rotation;
            }
            set
            {
                if (follow.isFollow && follow.followRotation)
                {
                    follow.eulerAnglesOffset = value.eulerAngles;
                }
                else
                {
                    camera.transform.rotation = value;
                }
            }
        }

        public SubCameraData(string name, string displayName, Camera camera, Rect viewportRect)
        {
            this.name = name;
            this.displayName = displayName;
            this.camera = camera;
            this.viewportRect = viewportRect;
        }

        public void ApplyViewport(Rect rect)
        {
            viewportRect = rect;
            if (camera != null)
            {
                camera.rect = rect;
            }
        }
    }

    public class SubCameraManager : ManagerBase
    {
        public const int MinSubCameraCount = 1;
        public const int MaxSubCameraCount = 8;
        private const string CameraNamePrefix = "SubCamera";
        public static readonly Rect DefaultViewport = new Rect(0f, 0.1f, 0.35f, 0.35f);

        // プラグイン無効化時のカメラ数。レイヤー未作成なら0のため再有効化時も生成しない
        private int _restoreCameraCount = 0;

        private List<SubCameraData> _subCameras = new List<SubCameraData>();
        private Dictionary<string, SubCameraData> _subCameraMap = new Dictionary<string, SubCameraData>();
        public List<string> subCameraNames = new List<string>();

        public List<SubCameraData> subCameras => _subCameras;

        public static event UnityAction<SubCameraData> onCameraAdded;
        public static event UnityAction<SubCameraData> onCameraRemoved;
        public static event UnityAction<SubCameraData> onCameraUpdated;

        private static SubCameraManager _instance = null;
        public static SubCameraManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SubCameraManager();
                }
                return _instance;
            }
        }

        private SubCameraManager()
        {
        }

        public static string GetCameraName(int index)
        {
            return CameraNamePrefix + (index + 1);
        }

        public static string GetCameraDisplayName(int index)
        {
            return "サブカメラ" + (index + 1);
        }

        public override void OnPluginDisable()
        {
            base.OnPluginDisable();

            _restoreCameraCount = _subCameras.Count;
            DestroyAllCameras();
        }

        /// <summary>
        /// 再有効化でカメラを無効化前と同数まで戻す。
        /// _restoreCameraCount は無効化時に控えた台数なので、復元先は OnLoad ではなくここ
        /// (タイムラインをアンロードすると OnLoad は timeline == null で呼ばれない)
        /// </summary>
        public override void OnPluginEnable()
        {
            base.OnPluginEnable();

            EnsureCameraCount(_restoreCameraCount);
        }

        // サブカメラレイヤーの作成時に最低数を確保する
        public void SetupCameras()
        {
            EnsureCameraCount(MinSubCameraCount);
        }

        private void EnsureCameraCount(int count)
        {
            while (_subCameras.Count < count)
            {
                if (AddNewCamera() == null)
                {
                    break;
                }
            }
        }

        /// <summary>サブカメラ数を指定数へ増減する (タイムライン設定ウィンドウの要素数 UI 用)</summary>
        public void SetCameraCount(int count)
        {
            count = Mathf.Clamp(count, MinSubCameraCount, MaxSubCameraCount);

            EnsureCameraCount(count);

            while (_subCameras.Count > count)
            {
                RemoveLastCamera();
            }
        }

        // SE 適合: GameViewManager のウィンドウモードではメインカメラが RenderTexture へ
        // リダイレクトされるため、サブカメラの出力先も毎フレーム追従させる。
        // targetTexture 内でも camera.rect は正規化 viewport として機能し、
        // PIP は GameViewWindow 内に正しく合成される。
        // 無効 (enabled=false) のカメラも対象に含め、再有効化時の出力先不整合を防ぐ
        public override void LateUpdate()
        {
            base.LateUpdate();

            var mainCam = PluginUtils.MainCamera;
            if (mainCam == null)
            {
                return;
            }
            var target = mainCam.targetTexture;
            foreach (var data in _subCameras)
            {
                if (data.camera == null)
                {
                    continue;
                }
                // 破棄済み RT は Unity の演算子オーバーロードで null と等価判定されるため、
                // != 比較だと破棄済み参照の掃除がスキップされる。参照同一性で比較する
                if (!ReferenceEquals(data.camera.targetTexture, target))
                {
                    data.camera.targetTexture = target;
                }
            }
        }

        public SubCameraData GetOrCreateCamera(string name)
        {
            var cameraData = GetCamera(name);
            if (cameraData != null)
            {
                return cameraData;
            }

            // タイムライン読込時など、キーフレームのカメラ名から不足分を生成する
            int no;
            if (!name.StartsWith(CameraNamePrefix) ||
                !int.TryParse(name.Substring(CameraNamePrefix.Length), out no))
            {
                return null;
            }

            // 外部データ由来の名前で無制限に生成されないよう上限を設ける
            if (no < 1 || no > MaxSubCameraCount)
            {
                MTEUtils.LogWarning("サブカメラ番号が不正のため生成をスキップしました name={0}", name);
                return null;
            }

            while (_subCameras.Count < no)
            {
                if (AddNewCamera() == null)
                {
                    return null;
                }
            }
            return GetCamera(name);
        }

        public SubCameraData AddNewCamera()
        {
            if (_subCameras.Count >= MaxSubCameraCount)
            {
                MTEUtils.LogWarning("サブカメラ数が上限に達しています max={0}", MaxSubCameraCount);
                return null;
            }

            var index = _subCameras.Count;
            var cameraData = CreateSubCamera(
                GetCameraName(index), GetCameraDisplayName(index), DefaultViewport);

            if (cameraData != null && onCameraAdded != null)
            {
                onCameraAdded(cameraData);
            }
            return cameraData;
        }

        public void RemoveLastCamera()
        {
            if (_subCameras.Count <= MinSubCameraCount)
            {
                return;
            }

            var cameraData = _subCameras[_subCameras.Count - 1];

            _subCameras.Remove(cameraData);
            _subCameraMap.Remove(cameraData.name);
            subCameraNames.Remove(cameraData.name);

            if (cameraData.camera != null)
            {
                Object.Destroy(cameraData.camera.gameObject);
            }

            if (onCameraRemoved != null)
            {
                onCameraRemoved(cameraData);
            }
        }

        public void DestroyAllCameras()
        {
            foreach (var cameraData in _subCameras)
            {
                if (cameraData.camera != null)
                {
                    Object.Destroy(cameraData.camera.gameObject);
                }
            }
            _subCameras.Clear();
            _subCameraMap.Clear();
            subCameraNames.Clear();
        }

        public SubCameraData GetCamera(string name)
        {
            SubCameraData camera;
            if (_subCameraMap.TryGetValue(name, out camera))
            {
                return camera;
            }
            return null;
        }

        private SubCameraData CreateSubCamera(string name, string displayName, Rect viewportRect)
        {
            var mainCam = PluginUtils.MainCamera;
            if (mainCam == null)
            {
                MTEUtils.LogWarning("メインカメラが見つからないためサブカメラを作成できません name={0}", name);
                return null;
            }

            var cameraObj = new GameObject(name);
            var newCamera = cameraObj.AddComponent<Camera>();

            newCamera.orthographic = mainCam.orthographic;
            newCamera.orthographicSize = mainCam.orthographicSize;
            newCamera.transform.position = mainCam.transform.position;
            newCamera.transform.rotation = mainCam.transform.rotation;
            newCamera.fieldOfView = mainCam.fieldOfView;
            newCamera.nearClipPlane = mainCam.nearClipPlane;
            newCamera.farClipPlane = mainCam.farClipPlane;
            newCamera.depth = mainCam.depth + 1;
            newCamera.cullingMask = mainCam.cullingMask;
            newCamera.clearFlags = mainCam.clearFlags;
            newCamera.allowHDR = mainCam.allowHDR;
            newCamera.allowMSAA = mainCam.allowMSAA;
            newCamera.rect = viewportRect;

            // 初期状態は有効。以降はキーフレームのEnableで制御する
            newCamera.enabled = true;

            var cameraData = new SubCameraData(name, displayName, newCamera, viewportRect);

            _subCameras.Add(cameraData);
            _subCameraMap.Add(name, cameraData);
            subCameraNames.Add(name);

            return cameraData;
        }

        public void UpdateCameraViewport(string name, Rect newViewportRect)
        {
            var cameraData = GetCamera(name);
            if (cameraData != null && cameraData.camera != null)
            {
                cameraData.ApplyViewport(newViewportRect);

                if (onCameraUpdated != null)
                {
                    onCameraUpdated(cameraData);
                }
            }
        }
    }
}
