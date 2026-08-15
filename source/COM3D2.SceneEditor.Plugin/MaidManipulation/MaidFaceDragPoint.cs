using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 頭部のドラッグ点。通常ドラッグで顔向き（首ボーンの回転）、
    /// Alt+Ctrl ドラッグで瞳の向きを操作する（MultipleMaids の gHead 相当）。
    /// 回すのは Bip01 Head ではなく Bip01 Neck で、これは MM と同じ
    /// </summary>
    public class MaidFaceDragPoint : MonoBehaviour, IMaidDragPoint
    {
        /// <summary>顔向きの感度。MM の MouseDrag3（ido==1）と同じ除数</summary>
        private const float HeadPitchDivisor = 3f;
        private const float HeadYawDivisor = 4.5f;

        /// <summary>瞳の感度。MM の MouseDrag3（ido==7）と同じ除数</summary>
        private const float EyeDivisor = 10f;

        public Maid maid;
        public Transform neckBone;    // "Bip01 Neck"
        public Transform headBone;    // "Bip01 Head"（追従位置の算出用）
        public Transform headNubBone; // "Bip01 HeadNub"（同上）

        private bool _isDragging = false;

        /// <summary>ドラッグ開始時に瞳モードだったか。途中でキーを離しても切り替わらないよう固定する</summary>
        private bool _isEyeMode = false;

        /// <summary>ドラッグ中の座標変換に使うカメラ。掴んだ側を覚えて二重駆動を防ぐ</summary>
        private Camera _dragCamera = null;

        /// <summary>これ以下の移動量ならドラッグではなくクリックとみなす (px)</summary>
        private const float ClickThresholdPixels = 5f;

        private Vector3 _mouseDownPos;
        private Vector3 _baseNeckAngles;
        private Vector3 _baseEyeAnglesL;
        private Vector3 _baseEyeAnglesR;

        private bool IsReady()
        {
            return maid != null && maid.body0 != null && neckBone != null;
        }

        /// <summary>コードベース共通の参照経路。Camera.main はシーン走査を伴うため使わない</summary>
        private static Camera GetGameCamera()
        {
            var mainCamera = GameMain.Instance.MainCamera;
            return mainCamera != null ? mainCamera.camera : null;
        }

        public bool BeginDrag(Camera camera, Vector3 pointerPos)
        {
            if (_isDragging || !IsReady() || camera == null || !canDrag)
            {
                return false;
            }

            _dragCamera = camera;
            _mouseDownPos = pointerPos;
            _isEyeMode = IsEyeModifierHeld();

            _baseNeckAngles = neckBone.localEulerAngles;
            _baseEyeAnglesL = maid.body0.quaDefEyeL.eulerAngles;
            _baseEyeAnglesR = maid.body0.quaDefEyeR.eulerAngles;

            MaidMotionState.StopMotion(maid);

            // 目線モードは quaDefEye のみで、ボーンは首だけ記録すればよい
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                _isEyeMode ? "目線操作" : "顔向き操作",
                _isEyeMode ? null : new[] { neckBone });

            // 追従が効いたままだと LateUpdate で上書きされるため切る
            maid.body0.boHeadToCam = false;
            maid.body0.boEyeToCam = false;

            _isDragging = true;

            // 瞳モードはボーンを回さないので追従させない
            if (!_isEyeMode)
            {
                MaidDragBoneTracker.BeginDrag(neckBone.name);
            }
            return true;
        }

        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || !IsReady())
            {
                return;
            }

            // MM と同じく押下位置からの総移動量で決める（フレーム差分の積算だと誤差が溜まる）
            var delta = pointerPos - _mouseDownPos;

            if (_isEyeMode)
            {
                ApplyEyeRotation(delta);
                return;
            }

            ApplyHeadRotation(delta);
        }

        public void EndDrag(Vector3 pointerPos)
        {
            if (!_isDragging)
            {
                return;
            }

            var downPos = _mouseDownPos;
            var wasEyeMode = _isEyeMode;
            CancelDrag();

            // クリック（微小移動）なら首を Inspector の選択対象にする。
            // 目線操作は首を回していないので選択を変えない
            if (!wasEyeMode && (pointerPos - downPos).magnitude <= ClickThresholdPixels)
            {
                SelectNeckInInspector();
            }
        }

        public void CancelDrag()
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            _dragCamera = null;
            MaidDragBoneTracker.EndDrag();
        }

        /// <summary>顔向きの実体は首の回転なので、Inspector には「首」のスライダーを出す</summary>
        private void SelectNeckInInspector()
        {
            var def = MaidBoneSliderController.FindDef(neckBone.name);
            if (def == null)
            {
                return;
            }

            SelectionManager.instance.SelectBone(maid, def);
            InspectorWindow.instance.isShowWnd = true;
        }

        private void OnMouseDown()
        {
            BeginDrag(GetGameCamera(), Input.mousePosition);
        }

        // SceneView 発のドラッグをゲーム画面側のマウスメッセージで動かさないよう掴んだ側を確認する
        private void OnMouseDrag()
        {
            if (_dragCamera != GetGameCamera())
            {
                return;
            }

            UpdateDrag(Input.mousePosition);
        }

        private void OnMouseUp()
        {
            if (_dragCamera != GetGameCamera())
            {
                return;
            }

            EndDrag(Input.mousePosition);
        }

        /// <summary>首ボーンを掴んだカメラ基準の水平軸・前後軸で回す（MouseDrag3 ido==1 と同型）</summary>
        private void ApplyHeadRotation(Vector3 delta)
        {
            if (_dragCamera == null)
            {
                return;
            }

            var cameraTransform = _dragCamera.transform;
            var right = cameraTransform.TransformDirection(Vector3.right);
            var forward = cameraTransform.TransformDirection(Vector3.forward);

            neckBone.localEulerAngles = _baseNeckAngles;
            neckBone.RotateAround(neckBone.position,
                new Vector3(right.x, 0f, right.z), delta.y / HeadPitchDivisor);
            neckBone.RotateAround(neckBone.position,
                new Vector3(forward.x, 0f, forward.z), -delta.x / HeadYawDivisor);
        }

        /// <summary>左右の瞳を逆向きに振って寄り目にならないようにする（MouseDrag3 ido==7 と同型）</summary>
        private void ApplyEyeRotation(Vector3 delta)
        {
            var yaw = delta.x / EyeDivisor;
            var pitch = delta.y / EyeDivisor;

            maid.body0.quaDefEyeR.eulerAngles = new Vector3(
                _baseEyeAnglesR.x, _baseEyeAnglesR.y + yaw, _baseEyeAnglesR.z + pitch);
            maid.body0.quaDefEyeL.eulerAngles = new Vector3(
                _baseEyeAnglesL.x, _baseEyeAnglesL.y - yaw, _baseEyeAnglesL.z - pitch);
        }

        private static bool IsAltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        /// <summary>
        /// 頭のボーンギズモ（Alt グループ）と取り合いにならないよう、Alt 中は顔向きを回さない。
        /// 目線は Alt+Ctrl 固定なのでこちらは Alt 中でも受け付ける
        /// </summary>
        public bool canDrag
        {
            get { return IsEyeModifierHeld() || !IsAltHeld(); }
        }

        /// <summary>頭部は IK 固定の対象外</summary>
        public bool isHeld
        {
            get { return false; }
        }

        private static bool IsEyeModifierHeld()
        {
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            return alt && ctrl;
        }

        private void OnDestroy()
        {
            // ドラッグ中に破棄された場合に掴み中表示が残らないようにする
            if (_isDragging)
            {
                MaidDragBoneTracker.EndDrag();
            }
        }

        private void LateUpdate()
        {
            // ドラッグ中も追従させる（首が回ると頭の位置も動くため）
            if (headBone == null || headNubBone == null)
            {
                return;
            }

            // 頭頂寄りに置く（MM が gHead の位置を決めるときと同じ重み付け）
            transform.position = new Vector3(
                headBone.position.x,
                (headBone.position.y * 1.2f + headNubBone.position.y * 0.8f) / 2f,
                headBone.position.z);
        }
    }
}
