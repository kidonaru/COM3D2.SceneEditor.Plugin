using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 頭・首のドラッグ点。通常ドラッグで rotateBone を傾け、
    /// Ctrl ドラッグで横方向の回転軸を鉛直軸（左右の振り向き）に切り替える（MultipleMaids の gHead 相当）。
    /// 頭の点（isHead）だけ、Alt+Ctrl ドラッグで瞳の向きを操作する。
    /// MM の gHead は首を回すが、ここでは頭の点が Bip01 Head、首の点が Bip01 Neck を回す
    /// </summary>
    public class MaidFaceDragPoint : MonoBehaviour, IMaidDragPoint
    {
        /// <summary>顔向きの感度。MM の MouseDrag3（ido==1）と同じ除数</summary>
        private const float HeadPitchDivisor = 3f;
        private const float HeadYawDivisor = 4.5f;

        /// <summary>瞳の感度。MM の MouseDrag3（ido==7）と同じ除数</summary>
        private const float EyeDivisor = 10f;

        /// <summary>
        /// 首の点を首から頭へ寄せる割合。首の原点は付け根にあり上体・肩の点と近いため、
        /// 頭との中点に置いて首の見た目の中央で掴ませる（回転の支点は首の原点のまま）
        /// </summary>
        private const float NeckPointHeadWeight = 0.5f;

        public Maid maid;
        public Transform rotateBone;  // 回すボーン。頭の点は "Bip01 Head"、首の点は "Bip01 Neck"
        public Transform headBone;    // "Bip01 Head"（追従位置の算出用）
        public Transform headNubBone; // "Bip01 HeadNub"（頭の点の追従位置の算出用）

        /// <summary>頭の点か。瞳操作・頭頂寄りの配置・顔追従の即時停止は頭の点だけ</summary>
        public bool isHead;

        private bool _isDragging = false;

        /// <summary>ドラッグ開始時に瞳モードだったか。途中でキーを離しても切り替わらないよう固定する</summary>
        private bool _isEyeMode = false;

        /// <summary>
        /// ドラッグ開始時に Ctrl が押されていたか。横ドラッグを鉛直軸まわりの回転にする。
        /// Alt 併用時は canDrag で掴めない（Alt+Ctrl は瞳モード）ので、有効なのは Ctrl 単独のとき
        /// </summary>
        private bool _isYawMode = false;

        /// <summary>ドラッグ中の座標変換に使うカメラ。掴んだ側を覚えて二重駆動を防ぐ</summary>
        private Camera _dragCamera = null;

        /// <summary>これ以下の移動量ならドラッグではなくクリックとみなす (px)</summary>
        private const float ClickThresholdPixels = 5f;

        private Vector3 _mouseDownPos;
        private Vector3 _baseBoneAngles;
        private Vector3 _baseEyeAnglesL;
        private Vector3 _baseEyeAnglesR;

        /// <summary>
        /// 履歴に記録するボーン。首の点は顔追従のフェードを待つので、その間に追従で動く頭も記録して
        /// Undo で確実に戻す (頭の点は割合を 0 にして止めるので頭だけでよい)
        /// </summary>
        private Transform[] GetHistoryBones()
        {
            if (isHead || headBone == null)
            {
                return new[] { rotateBone };
            }
            return new[] { rotateBone, headBone };
        }

        private bool IsReady()
        {
            return maid != null && maid.body0 != null && rotateBone != null;
        }

        /// <summary>コードベース共通の参照経路。Camera.main はシーン走査を伴うため使わない</summary>
        private static Camera GetGameCamera()
        {
            var mainCamera = GameMain.Instance.MainCamera;
            return mainCamera != null ? mainCamera.camera : null;
        }

        public bool BeginDrag(Camera camera, Vector3 pointerPos)
        {
            if (MaidAnimationBlendController.IsLayerSelected(maid))
            {
                // レイヤー調整中は掴ませない。点の実体は作らせていないが、
                // 破棄前のフレームに掴まれても書き込ませないための 2 段目
                return false;
            }

            if (_isDragging || !IsReady() || camera == null || !canDrag)
            {
                return false;
            }

            _dragCamera = camera;
            _mouseDownPos = pointerPos;
            // 瞳は頭の点だけ。首の点は canDrag で Alt 中は掴めないので、ここに来るのは Alt なしのとき
            _isEyeMode = isHead && IsEyeModifierHeld();
            _isYawMode = !_isEyeMode && IsCtrlHeld();

            _baseBoneAngles = rotateBone.localEulerAngles;
            _baseEyeAnglesL = maid.body0.quaDefEyeL.eulerAngles;
            _baseEyeAnglesR = maid.body0.quaDefEyeR.eulerAngles;

            MaidMotionState.StopMotion(maid);

            // 目線モードは quaDefEye のみで、ボーンは回すボーンだけ記録すればよい
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                _isEyeMode ? "目線操作" : (isHead ? "顔向き操作" : "首操作"),
                _isEyeMode ? null : GetHistoryBones());

            // 追従が効いたままだと LateUpdate で上書きされるため切る。戻すのは「メイド目線」の選び直し
            maid.body0.boHeadToCam = false;
            maid.body0.boEyeToCam = false;
            if (isHead && !_isEyeMode)
            {
                // TBody.MoveHeadAndEye は割合 HeadToCamPer で頭の localRotation を Slerp で上書きする。
                // フラグを倒すだけではフェードし終えるまで回した頭が引き戻されるため、割合も 0 にする
                // (頭ギズモの StopHeadToCamWhileGrabbingHead と同じ)。首はこの Slerp の対象外
                maid.body0.HeadToCamPer = 0f;
            }

            _isDragging = true;

            // 瞳モードはボーンを回さないので追従させない。
            // 書き換えるのは quaDefEye だけでアニメがサンプルするボーンには触れないため、
            // アニメブレンドの解除も要らない
            if (!_isEyeMode)
            {
                MaidDragBoneTracker.BeginDrag(maid, rotateBone.name);
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
            MaidDragBoneTracker.NotifyDragCompleted(maid);

            // クリック（微小移動）なら回すボーンを Inspector の選択対象にする。
            // 目線操作はボーンを回していないので選択を変えない
            if (!wasEyeMode && (pointerPos - downPos).magnitude <= ClickThresholdPixels)
            {
                SelectBoneInInspector();
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

        /// <summary>回したボーン（頭 / 首）のスライダーを Inspector に出す</summary>
        private void SelectBoneInInspector()
        {
            var def = MaidBoneSliderController.FindDef(rotateBone.name);
            if (def == null)
            {
                return;
            }

            SelectionManager.instance.SelectBone(maid, def);
            InspectorWindow.instance.isShowWnd = true;
        }

        private void OnMouseDown()
        {
            // ゲーム画面で見えていない白丸は掴ませない
            if (!MaidManipulateManager.instance.isGameViewDragPointVisible)
            {
                return;
            }
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

        /// <summary>
        /// rotateBone を掴んだカメラ基準の水平軸・前後軸で回す（MouseDrag3 ido==1 と同型）。
        /// Ctrl モードでは横ドラッグの軸を前後軸から鉛直軸に替え、通常ドラッグでは出せない左右の振り向きにする
        /// </summary>
        private void ApplyHeadRotation(Vector3 delta)
        {
            if (_dragCamera == null)
            {
                return;
            }

            var cameraTransform = _dragCamera.transform;
            var right = cameraTransform.TransformDirection(Vector3.right);
            var forward = cameraTransform.TransformDirection(Vector3.forward);

            rotateBone.localEulerAngles = _baseBoneAngles;
            rotateBone.RotateAround(rotateBone.position,
                new Vector3(right.x, 0f, right.z), delta.y / HeadPitchDivisor);
            var yawAxis = _isYawMode ? Vector3.up : new Vector3(forward.x, 0f, forward.z);
            rotateBone.RotateAround(rotateBone.position, yawAxis, -delta.x / HeadYawDivisor);
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

        private static bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>
        /// 頭のボーンギズモ（Alt グループ）と取り合いにならないよう、Alt 中はボーンを回さない。
        /// 目線は Alt+Ctrl 固定で頭の点だけなので、頭の点はそのときだけ Alt 中でも受け付ける。
        /// Shift（移動・肘膝固定）に対応する操作は無いので Shift 中も掴ませない
        /// </summary>
        public bool canDrag
        {
            get { return (isHead && IsEyeModifierHeld()) || (!IsAltHeld() && !IsShiftHeld()); }
        }

        /// <summary>頭・首は IK 固定の対象外</summary>
        public bool isHeld
        {
            get { return false; }
        }

        private static bool IsEyeModifierHeld()
        {
            return IsAltHeld() && IsCtrlHeld();
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
            // ドラッグ中も追従させる（首・頭が回ると点の位置も動くため）
            if (headBone == null)
            {
                return;
            }

            if (isHead)
            {
                UpdateHeadPointPosition();
            }
            else
            {
                UpdateNeckPointPosition();
            }
        }

        /// <summary>頭頂寄りに置く（MM が gHead の位置を決めるときと同じ重み付け）</summary>
        private void UpdateHeadPointPosition()
        {
            if (headNubBone == null)
            {
                return;
            }

            transform.position = new Vector3(
                headBone.position.x,
                (headBone.position.y * 1.2f + headNubBone.position.y * 0.8f) / 2f,
                headBone.position.z);
        }

        private void UpdateNeckPointPosition()
        {
            if (rotateBone == null)
            {
                return;
            }

            transform.position = Vector3.Lerp(rotateBone.position, headBone.position, NeckPointHeadWeight);
        }
    }
}
