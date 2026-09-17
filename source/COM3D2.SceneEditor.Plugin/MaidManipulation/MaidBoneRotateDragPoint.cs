using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン回転用の汎用ドラッグ点（MultipleMaids の MouseDrag3/MouseDrag4 系を整理移植）。
    /// 通常ドラッグはカメラ基準の傾げ（重み付きで複数ボーンへ配分可能）、
    /// Ctrl ドラッグはローカル X 軸まわりのひねり。
    /// 上体（4 ボーン配分）・骨盤・手の甲（_IK_hand）をこの 1 クラスで扱う。
    /// <see cref="moveBone"/> を設定した点（骨盤）は Shift ドラッグでそのボーンを平行移動する
    /// </summary>
    public class MaidBoneRotateDragPoint : MonoBehaviour, IMaidDragPoint
    {
        /// <summary>回転対象 1 ボーンぶんの重み。twistWeight=0 のボーンはひねり対象外</summary>
        public struct Entry
        {
            public Transform bone;
            public float tiltWeight;
            public float twistWeight;

            public Entry(Transform bone, float tiltWeight, float twistWeight)
            {
                this.bone = bone;
                this.tiltWeight = tiltWeight;
                this.twistWeight = twistWeight;
            }
        }

        public Maid maid;
        public Entry[] entries;

        /// <summary>追従先。回転対象と別のボーンを指すこともある</summary>
        public Transform followBone;

        /// <summary>傾げの感度。delta.y / pitchDivisor、delta.x / yawDivisor（符号込み）</summary>
        public float pitchDivisor = 1f;
        public float yawDivisor = -1.5f;

        /// <summary>ひねりの感度。delta.x / twistDivisor</summary>
        public float twistDivisor = 1.5f;

        /// <summary>
        /// Shift ドラッグで平行移動するボーン（骨盤点なら中心 Bip01）。null なら移動モードなし。
        /// 中心は骨盤と同じ位置にあり専用の点を置くと重なって掴めないため、骨盤点に同居させる
        /// </summary>
        public Transform moveBone;

        private bool _isDragging = false;

        /// <summary>ドラッグ開始時にひねりモードだったか。途中でキーを離しても切り替わらないよう固定する</summary>
        private bool _isTwistMode = false;

        /// <summary>ドラッグ開始時に移動モードだったか。ひねりと同じく開始時点で固定する</summary>
        private bool _isMoveMode = false;

        private Vector3 _mouseDownPos;
        private Vector3[] _baseAngles;

        /// <summary>移動モードで掴んだ時点の moveBone のスクリーン座標。奥行き (z) を保つために使う</summary>
        private Vector3 _moveScreenPoint;

        /// <summary>移動モードでの、ポインタ位置をワールドへ戻した点から moveBone までのずれ</summary>
        private Vector3 _moveOffset;

        /// <summary>ドラッグ中の座標変換に使うカメラ。掴んだ側を覚えて二重駆動を防ぐ</summary>
        private Camera _dragCamera = null;

        /// <summary>これ以下の移動量ならドラッグではなくクリックとみなす (px)</summary>
        private const float ClickThresholdPixels = 5f;

        private bool IsReady()
        {
            return maid != null && maid.body0 != null && entries != null && entries.Length > 0;
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

        /// <summary>ボーンギズモを掴む操作と取り合いにならないよう、Alt 中はドラッグしない</summary>
        public bool canDrag
        {
            get { return !IsAltHeld(); }
        }

        /// <summary>上体・骨盤は IK 固定の対象外</summary>
        public bool isHeld
        {
            get { return false; }
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
            // Shift+Ctrl 同時押しは移動を優先する
            _isMoveMode = moveBone != null && IsShiftHeld();
            _isTwistMode = !_isMoveMode && IsCtrlHeld();

            MaidMotionState.StopMotion(maid);

            if (_isMoveMode)
            {
                BeginMove(camera, pointerPos);
            }
            else
            {
                BeginRotate();
            }

            _isDragging = true;
            MaidDragBoneTracker.BeginDrag(GetTrackedBoneName());
            return true;
        }

        private void BeginRotate()
        {
            _baseAngles = new Vector3[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                _baseAngles[i] = entries[i].bone.localEulerAngles;
            }

            var targetBones = new Transform[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                targetBones[i] = entries[i].bone;
            }
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ボーン回転: " + (followBone != null ? followBone.name : GetPrimaryBoneName()),
                targetBones);
        }

        /// <summary>
        /// 移動モードの開始。IK ドラッグ点と同じく、掴んだ時点の奥行きを保ったまま
        /// ポインタをワールドへ戻し、その点からボーンまでのずれを覚えておく
        /// </summary>
        private void BeginMove(Camera camera, Vector3 pointerPos)
        {
            _moveScreenPoint = camera.WorldToScreenPoint(moveBone.position);
            _moveOffset = moveBone.position - camera.ScreenToWorldPoint(
                new Vector3(pointerPos.x, pointerPos.y, _moveScreenPoint.z));

            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ボーン移動: " + moveBone.name, new[] { moveBone });
        }

        /// <summary>
        /// 掴み中として報告するボーン名。移動モードは移動対象、それ以外は追従先を優先する
        /// (共有 entries の代表ボーンだと上体の全点が同じ名前になるため)
        /// </summary>
        private string GetTrackedBoneName()
        {
            if (_isMoveMode)
            {
                return moveBone.name;
            }
            return followBone != null ? followBone.name : GetPrimaryBoneName();
        }

        /// <summary>
        /// 代表ボーン名。複数ボーンへ配分する点（上体）では最も重みの大きいボーンを代表とする
        /// </summary>
        private string GetPrimaryBoneName()
        {
            var primary = entries[0];
            for (var i = 1; i < entries.Length; i++)
            {
                if (entries[i].tiltWeight > primary.tiltWeight)
                {
                    primary = entries[i];
                }
            }
            return primary.bone != null ? primary.bone.name : null;
        }

        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || !IsReady())
            {
                return;
            }

            if (_isMoveMode)
            {
                ApplyMove(pointerPos);
                return;
            }

            // MM と同じく押下位置からの総移動量で決める（フレーム差分の積算だと誤差が溜まる）
            var delta = pointerPos - _mouseDownPos;

            if (_isTwistMode)
            {
                ApplyTwist(delta);
                return;
            }

            ApplyTilt(delta);
        }

        public void EndDrag(Vector3 pointerPos)
        {
            if (!_isDragging)
            {
                return;
            }

            var downPos = _mouseDownPos;
            CancelDrag();
            MaidDragBoneTracker.NotifyDragCompleted(maid);

            // クリック（微小移動）ならボーンを Inspector の選択対象にする
            if ((pointerPos - downPos).magnitude <= ClickThresholdPixels)
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

        /// <summary>掴んだカメラ基準の水平軸・前後軸まわりに重み配分して回す（MouseDrag3 ido==2/3 と同型）</summary>
        private void ApplyTilt(Vector3 delta)
        {
            if (_dragCamera == null)
            {
                return;
            }

            var cameraTransform = _dragCamera.transform;
            var right = cameraTransform.TransformDirection(Vector3.right);
            var forward = cameraTransform.TransformDirection(Vector3.forward);

            // 親を回すと子の位置が動くため、全ボーンを基準姿勢へ戻してから順に回す（MM と同じ手順）
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i].bone.localEulerAngles = _baseAngles[i];
            }

            for (var i = 0; i < entries.Length; i++)
            {
                var bone = entries[i].bone;
                var weight = entries[i].tiltWeight;

                bone.RotateAround(bone.position,
                    new Vector3(right.x, 0f, right.z), delta.y / pitchDivisor * weight);
                bone.RotateAround(bone.position,
                    new Vector3(forward.x, 0f, forward.z), delta.x / yawDivisor * weight);
            }
        }

        /// <summary>カメラに正対する平面上でボーンを平行移動する（IK ドラッグ点と同じ座標変換）</summary>
        private void ApplyMove(Vector3 pointerPos)
        {
            if (_dragCamera == null || moveBone == null)
            {
                return;
            }

            var pos = new Vector3(pointerPos.x, pointerPos.y, _moveScreenPoint.z);
            moveBone.position = _dragCamera.ScreenToWorldPoint(pos) + _moveOffset;
        }

        /// <summary>ローカル X 軸まわりのひねり（MouseDrag3 ido==5/6 と同型）</summary>
        private void ApplyTwist(Vector3 delta)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                var weight = entries[i].twistWeight;
                if (weight == 0f)
                {
                    continue;
                }

                entries[i].bone.localRotation = Quaternion.Euler(_baseAngles[i])
                    * Quaternion.AngleAxis(delta.x / twistDivisor * weight, Vector3.right);
            }
        }

        /// <summary>掴んだ点自身のボーンを選択し、Inspector を専用表示で開く</summary>
        private void SelectBoneInInspector()
        {
            // Shift クリックなら移動対象、それ以外は追従先ボーンを選ぶ
            var boneName = GetTrackedBoneName();
            var def = MaidBoneSliderController.FindDef(boneName)
                ?? MaidBoneSliderController.FindDef(GetPrimaryBoneName());
            if (def == null)
            {
                return;
            }

            SelectionManager.instance.SelectBone(maid, def);
            InspectorWindow.instance.isShowWnd = true;
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
            if (followBone != null)
            {
                transform.position = followBone.position;
            }
        }
    }
}
