using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メインカメラ (UltimateOrbitCamera) の注視点をメイドへ追従させる。
    /// オービットモデルのため Transform ではなく注視点を追従点 + オフセットに置き、
    /// 向き反映時はヨーを「メイドの向き + ヨーオフセット」にする (ピッチ・距離・ロールは絶対値のまま)。
    /// UltimateOrbitCamera は Update で位置を確定するため、LateUpdate で注視点を書いた後に
    /// 同じ式でカメラ位置を再計算し、1 フレーム遅れを防ぐ
    /// </summary>
    public class MaidFollowMainCamera : MonoBehaviour
    {
        public readonly MaidFollowState state = new MaidFollowState();

        private static Camera camera => PluginUtils.MainCamera;

        private static MaidFollowMainCamera _instance = null;
        public static MaidFollowMainCamera instance
        {
            get
            {
                if (_instance == null && camera != null)
                {
                    _instance = camera.gameObject.GetOrAddComponent<MaidFollowMainCamera>();
                }
                return _instance;
            }
        }

        public bool isFollow => state.isFollow;

        private void LateUpdate()
        {
            Apply();
        }

        /// <summary>追従位置を即時反映する。UI で設定を変えた直後にも呼べる</summary>
        public void Apply()
        {
            var camera = MaidFollowMainCamera.camera;
            if (camera == null)
            {
                return;
            }

            var uoCamera = camera.GetComponent<UltimateOrbitCamera>();
            if (uoCamera == null || uoCamera.target == null)
            {
                return;
            }

            Vector3 anchor;
            Quaternion faceRotation;
            if (!state.TryGetAnchor(out anchor, out faceRotation))
            {
                return;
            }

            uoCamera.SetTargetPos(state.GetFollowPosition(anchor, faceRotation));

            if (state.followRotation)
            {
                var aroundAngle = uoCamera.GetAroundAngle();
                aroundAngle.x = faceRotation.eulerAngles.y + state.yawOffset;
                uoCamera.SetAroundAngle(aroundAngle);
            }

            // UltimateOrbitCamera.Update の基本式で位置を確定する。
            // 本来はスクリーンオフセット (offsetNow / m_worldOffset) の補正も入るが、
            // 本プラグインの利用シーンでは常にゼロ (実機確認済み) のため省いている
            camera.transform.position =
                camera.transform.rotation * new Vector3(0f, 0f, -uoCamera.distance)
                + uoCamera.target.position;
        }
    }
}
