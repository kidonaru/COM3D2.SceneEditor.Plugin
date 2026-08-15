using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メインカメラの構図スナップショット。
    /// SceneView カメラは「見え方」であり履歴の対象にしない
    /// </summary>
    public class CameraSnapshot : IStateSnapshot
    {
        private ScenePresetCamera _state;

        public static CameraSnapshot Capture()
        {
            return new CameraSnapshot { _state = CaptureState() };
        }

        /// <summary>現在のメインカメラの構図を記録する。取れなければ null</summary>
        public static ScenePresetCamera CaptureState()
        {
            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return null;
            }

            var aroundAngle = mainCamera.GetAroundAngle();
            return new ScenePresetCamera
            {
                targetPos = mainCamera.GetTargetPos(),
                yaw = aroundAngle.x,
                pitch = aroundAngle.y,
                roll = camera.transform.eulerAngles.z,
                distance = mainCamera.GetDistance(),
                fov = camera.fieldOfView,
            };
        }

        public static void ApplyState(ScenePresetCamera state)
        {
            if (state == null)
            {
                return;
            }

            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return;
            }

            mainCamera.SetTargetPos(state.targetPos);
            mainCamera.SetAroundAngle(new Vector2(state.yaw, state.pitch));
            mainCamera.SetDistance(state.distance);
            camera.fieldOfView = state.fov;

            // ロールはオービットモデル外なので、旋回角を確定させた後に Transform へ直接書く
            var eulerAngles = camera.transform.eulerAngles;
            eulerAngles.z = state.roll;
            camera.transform.eulerAngles = eulerAngles;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid) => ApplyState(_state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as CameraSnapshot;
            if (o == null || _state == null || o._state == null)
            {
                return false;
            }

            return _state.targetPos == o._state.targetPos
                && Mathf.Approximately(_state.yaw, o._state.yaw)
                && Mathf.Approximately(_state.pitch, o._state.pitch)
                && Mathf.Approximately(_state.roll, o._state.roll)
                && Mathf.Approximately(_state.distance, o._state.distance)
                && Mathf.Approximately(_state.fov, o._state.fov);
        }

        public bool CanApply(Maid maid) => _state != null;
    }
}
