using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 指関節用の軸拘束ドラッグ点。カメラ基準の自由回転（MaidBoneRotateDragPoint）だと
    /// 指では意図しない横曲げ・ひねりが混ざり方向が予測できないため、
    /// マウス上下＝曲げ（ローカル Z 軸）、左右＝開き（根本関節のローカル Y 軸）に固定する。
    /// 曲げ軸は指ブレンドテンプレート（open→fist）の実測で手指 -Z / 足指 +Z、
    /// 開き軸は close→open の実測でローカル Y と確認済み。
    /// 可動域はテンプレート由来の範囲（＋マージン）でクランプし、逆反りや一回転を防ぐ
    /// </summary>
    public class MaidFingerDragPoint : MonoBehaviour, IMaidDragPoint
    {
        /// <summary>曲げ対象 1 ボーンぶんの重みと可動域。指先のカール点は複数関節へ配分する</summary>
        public struct Entry
        {
            public Transform bone;
            public float weight;

            /// <summary>可動域クランプを行うか。テンプレートに無いボーンは無制限で動かす</summary>
            public bool hasLimit;

            /// <summary>曲げ角 0 度の基準姿勢（open テンプレートの回転）</summary>
            public Quaternion openRotation;

            public float bendMin;
            public float bendMax;
        }

        public Maid maid;
        public Entry[] entries;

        /// <summary>曲げのローカル軸。手指 (0,0,-1) / 足指 (0,0,1)</summary>
        public Vector3 bendAxis = new Vector3(0f, 0f, -1f);

        /// <summary>開き（スプレッド）を許可する根本関節。null なら左右ドラッグは無効</summary>
        public Transform spreadBone;

        /// <summary>開きの可動域。hasSpreadLimit が false なら無制限</summary>
        public bool hasSpreadLimit;
        public Quaternion spreadOpenRotation;
        public float spreadMin;
        public float spreadMax;

        /// <summary>追従先ボーン（点の表示位置）。指先カール点では Nub を指す</summary>
        public Transform followBone;

        /// <summary>曲げの感度。マウス下方向（delta.y 負）で曲がるよう負値にしてある</summary>
        public float bendDivisor = -3f;

        /// <summary>
        /// 開きの感度。可動域（35〜55 度程度）が狭いため曲げより鈍くする。
        /// 開き軸（ローカル Y）の符号は指によって逆なため、同じ右ドラッグでも
        /// 開く指と閉じる指がある（テンプレート実測でも指ごとに符号が異なる）
        /// </summary>
        public float spreadDivisor = 6f;

        private bool _isDragging = false;
        private Vector3 _mouseDownPos;
        private Quaternion[] _baseRotations;
        private Quaternion _baseSpreadRotation;

        /// <summary>ドラッグ開始時点の曲げ角（open テンプレート基準）と、今回許可する範囲。
        /// 開始時点で範囲外だったポーズをスナップさせないよう、範囲は開始角まで広げる</summary>
        private float[] _startBendAngles;
        private float[] _allowedBendMin;
        private float[] _allowedBendMax;

        private float _startSpreadAngle;
        private float _allowedSpreadMin;
        private float _allowedSpreadMax;

        /// <summary>ドラッグ中の座標変換に使うカメラ。掴んだ側を覚えて二重駆動を防ぐ</summary>
        private Camera _dragCamera = null;

        private bool IsReady()
        {
            return maid != null && maid.body0 != null && entries != null && entries.Length > 0;
        }

        private static bool IsAltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        /// <summary>ボーンギズモを掴む操作と取り合いにならないよう、Alt 中はドラッグしない</summary>
        public bool canDrag
        {
            get { return !IsAltHeld(); }
        }

        /// <summary>指は IK 固定の対象外</summary>
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

        /// <summary>
        /// 基準姿勢からの相対回転 q のうち、axis まわりのひねり成分の符号付き角度 (度)。
        /// 曲げ・開きが混ざった回転から片方の軸の角度だけ取り出すのに使う
        /// </summary>
        private static float TwistAngle(Quaternion q, Vector3 axis)
        {
            var proj = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            var angle = 2f * Mathf.Atan2(proj, q.w) * Mathf.Rad2Deg;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }
            return angle;
        }

        public bool BeginDrag(Camera camera, Vector3 pointerPos)
        {
            if (_isDragging || !IsReady() || camera == null || !canDrag)
            {
                return false;
            }

            _dragCamera = camera;
            _mouseDownPos = pointerPos;

            _baseRotations = new Quaternion[entries.Length];
            _startBendAngles = new float[entries.Length];
            _allowedBendMin = new float[entries.Length];
            _allowedBendMax = new float[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                var bone = entries[i].bone;
                _baseRotations[i] = bone.localRotation;

                if (entries[i].hasLimit)
                {
                    var start = TwistAngle(
                        Quaternion.Inverse(entries[i].openRotation) * bone.localRotation, bendAxis);
                    _startBendAngles[i] = start;
                    _allowedBendMin[i] = Mathf.Min(entries[i].bendMin, start);
                    _allowedBendMax[i] = Mathf.Max(entries[i].bendMax, start);
                }
            }

            if (spreadBone != null)
            {
                _baseSpreadRotation = spreadBone.localRotation;
                if (hasSpreadLimit)
                {
                    var start = TwistAngle(
                        Quaternion.Inverse(spreadOpenRotation) * _baseSpreadRotation, Vector3.up);
                    _startSpreadAngle = start;
                    _allowedSpreadMin = Mathf.Min(spreadMin, start);
                    _allowedSpreadMax = Mathf.Max(spreadMax, start);
                }
            }

            MaidMotionState.StopMotion(maid);

            var targetBones = new Transform[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                targetBones[i] = entries[i].bone;
            }
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "指編集: " + followBone.name, targetBones);

            _isDragging = true;
            MaidDragBoneTracker.BeginDrag(followBone.name);
            return true;
        }

        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || !IsReady())
            {
                return;
            }

            // ドラッグ中に着替え等でボーンが破棄されたら安全に打ち切る
            // (Destroy は遅延破棄のため、点の破棄より先にこちらが走る余地がある)
            if (followBone == null)
            {
                CancelDrag();
                return;
            }
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].bone == null)
                {
                    CancelDrag();
                    return;
                }
            }

            // MM と同じく押下位置からの総移動量で決める（フレーム差分の積算だと誤差が溜まる）
            var delta = pointerPos - _mouseDownPos;

            // 開きは曲げと独立に根本関節へだけ乗せる。根本が entries にも入っている場合、
            // まず開き成分を適用し、その上へ曲げを重ねる
            if (spreadBone != null)
            {
                var spreadDelta = delta.x / spreadDivisor;
                if (hasSpreadLimit)
                {
                    spreadDelta = ClampDelta(_startSpreadAngle, spreadDelta,
                        _allowedSpreadMin, _allowedSpreadMax);
                }
                spreadBone.localRotation = _baseSpreadRotation
                    * Quaternion.AngleAxis(spreadDelta, Vector3.up);
            }

            for (var i = 0; i < entries.Length; i++)
            {
                var bone = entries[i].bone;
                var bendDelta = delta.y / bendDivisor * entries[i].weight;
                if (entries[i].hasLimit)
                {
                    bendDelta = ClampDelta(_startBendAngles[i], bendDelta,
                        _allowedBendMin[i], _allowedBendMax[i]);
                }

                // spreadBone は上で開き適用済みの localRotation を base に曲げを重ねる
                var baseRotation = bone == spreadBone ? bone.localRotation : _baseRotations[i];
                bone.localRotation = baseRotation
                    * Quaternion.AngleAxis(bendDelta, bendAxis);
            }
        }

        /// <summary>開始角にドラッグ量を足した結果が許可範囲に収まるようドラッグ量を丸める</summary>
        private static float ClampDelta(float startAngle, float rawDelta, float min, float max)
        {
            return Mathf.Clamp(startAngle + rawDelta, min, max) - startAngle;
        }

        public void EndDrag(Vector3 pointerPos)
        {
            CancelDrag();
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
