using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// IK のドラッグ点。手首/足首・肘/膝・胸に置く。
    /// 透明な球コライダを骨に追従させ、掴んでいる間は自分自身が FABRIK の target になる。
    /// 解くのは MaidIKChain 側で、この点はマウス位置をワールド座標へ変換して置くだけ。
    /// 肩・手首の点を Ctrl で掴んだときは FABRIK を使わず、上腕・手を骨の軸まわりにロールする。
    /// 手首 / 足首の点を Shift で掴むと肘 / 膝を固定して解く
    /// </summary>
    public class MaidIKDragPoint : MonoBehaviour, IMaidDragPoint
    {
        public Maid maid;

        /// <summary>この点が属する IK チェーン</summary>
        public MaidIKChain chain;

        /// <summary>チェーンのどこを掴む点か</summary>
        public MaidIKChainPoint pointType = MaidIKChainPoint.Tip;

        /// <summary>非ドラッグ時に張り付く骨</summary>
        public Transform followBone;

        /// <summary>
        /// 設定されていれば followBone との中点に置く。
        /// 胸は先端（乳首位置）だと掴みにくいため根本との中点に置く（MM と同じ）
        /// </summary>
        public Transform followBoneSub;

        /// <summary>
        /// 胸のドラッグ点。掴んだら揺れ物（jiggleBone）を切らないと
        /// LateUpdate で上書きされて手付けが戻ってしまう
        /// </summary>
        public bool isMune = false;

        /// <summary>
        /// Ctrl で掴むと followBone をロールする点か。肩の点 (followBone = 上腕) と
        /// 手首の点 (followBone = 手。前腕のツイストボーンはゲームの AutoTwist が手の回転から追従させる) で立てる
        /// </summary>
        public bool canRoll = false;

        /// <summary>Shift で掴むと肘 / 膝を固定して解く点か。手首 / 足首の点で立てる</summary>
        public bool canJointLock = false;

        /// <summary>これ以下の移動量ならドラッグではなくクリックとみなす (px)</summary>
        private const float ClickThresholdPixels = 5f;

        /// <summary>この秒数以内の 2 回目のクリックをダブルクリックとみなす</summary>
        private const float DoubleClickTime = 0.3f;

        /// <summary>_lastClickTime が未クリック状態であることを示す値</summary>
        private const float NoClickTime = -1f;

        private bool _isDragging = false;
        private Vector3 _screenPoint;
        private Vector3 _offset;
        private Vector3 _mouseDownPos;

        /// <summary>直前のクリック確定時刻。ダブルクリック判定用</summary>
        private float _lastClickTime = NoClickTime;

        /// <summary>ロールの感度 (px / 度)。上体・骨盤の Ctrl ひねりの既定値と揃える</summary>
        private const float RollDivisor = MaidBoneRotateDragPoint.DefaultTwistDivisor;

        /// <summary>
        /// 掴んだ時点でロールモードだったか (肩・手首の点 + Ctrl)。
        /// 途中でキーを離しても切り替わらないよう開始時に固定する
        /// </summary>
        private bool _isRollMode = false;

        /// <summary>ロール開始時の followBone のローカル回転。押下位置からの総移動量をこれに掛ける</summary>
        private Quaternion _rollBaseRotation;

        /// <summary>
        /// ドラッグ中の座標変換に使うカメラ。ゲーム画面と SceneView で異なるため掴んだ側を覚えておく
        /// </summary>
        private Camera _dragCamera = null;

        /// <summary>
        /// 自動追従で報告する骨名の上書き。胸は followBone が先端（Mune_*_sub）のため、
        /// スライダー対象の根本（Mune_*）を指定する。null なら followBone の名前を使う
        /// </summary>
        public string sliderBoneName;

        /// <summary>胸の根本ボーンのうち左側の名前。左右の判別に使う</summary>
        private const string MuneLeftBoneName = "Mune_L";

        /// <summary>胸のドラッグ点のうち左側か。isMune が true のときだけ意味を持つ</summary>
        public bool isMuneLeft
        {
            get { return sliderBoneName == MuneLeftBoneName; }
        }

        private bool IsReady()
        {
            return maid != null && maid.body0 != null && chain != null && followBone != null;
        }

        /// <summary>コードベース共通の参照経路。Camera.main はシーン走査を伴うため使わない</summary>
        private static Camera GetCamera()
        {
            var mainCamera = GameMain.Instance.MainCamera;
            return mainCamera != null ? mainCamera.camera : null;
        }

        private static bool IsAltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        /// <summary>
        /// 修飾キーごとに、その操作を持つ点だけ掴ませる。
        /// Alt はボーンギズモと取り合わないよう掴まない。Shift は肘 / 膝の固定、Ctrl はロールができる点だけ
        /// </summary>
        public bool canDrag
        {
            get
            {
                if (IsAltHeld())
                {
                    return false;
                }
                if (IsShiftHeld())
                {
                    return canJointLock;
                }
                if (IsCtrlHeld())
                {
                    return canRoll;
                }
                return true;
            }
        }

        /// <summary>
        /// この点のボーンが IK 固定中か。固定は編集モード中しか効かないため、
        /// 実際に効いている状態だけを固定色で示す
        /// </summary>
        public bool isHeld
        {
            get
            {
                var manager = MaidManipulateManager.instance;
                if (maid == null || followBone == null || !manager.isEditMode)
                {
                    return false;
                }

                MaidIKHoldType holdType;
                return MaidIKHoldController.TryGetHoldType(followBone.name, out holdType)
                    && manager.ikHoldController.GetHold(maid, holdType);
            }
        }

        private static bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>
        /// Shift 押下中は肘/膝を固定する（Ctrl はひねり専用。骨盤の移動と同じ Shift）
        /// </summary>
        private static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>
        /// ドラッグ開始。掴めたら true。
        /// pointerPos は camera の描画面上の座標（ゲーム画面は Input.mousePosition、
        /// SceneView は RT 座標）で、ゲーム画面と SceneView の両方から呼ばれる
        /// </summary>
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
            _screenPoint = camera.WorldToScreenPoint(transform.position);
            _offset = transform.position - camera.ScreenToWorldPoint(
                new Vector3(pointerPos.x, pointerPos.y, _screenPoint.z));

            // Shift+Ctrl は Shift 側 (肘 / 膝の固定) を優先するので、ロールは Shift なしのときだけ
            _isRollMode = canRoll && IsCtrlHeld() && !IsShiftHeld();

            PrepareEdit(_isRollMode ? "ロール: " : "IK操作: ");

            // 掴んだ時点で IK 選択に切り替える。クリック確定 (EndDrag) まで待つと、
            // ボーン選択が生きたままドラッグが始まり Inspector のボーン自動追従に奪われる
            SelectionManager.instance.SelectIK(this);

            if (_isRollMode)
            {
                // ロールは FABRIK で解かない。チェーンを張ると target (この点) を追って鎖骨や肘が動いてしまう
                _rollBaseRotation = followBone.localRotation;
            }
            else
            {
                // 固定するかは掴んだ時点で決める。途中で Shift を離してもモードは変えない
                chain.BeginDrag(pointType, canJointLock && IsShiftHeld(), transform);
            }
            _isDragging = true;
            MaidDragBoneTracker.BeginDrag(maid, sliderBoneName ?? followBone.name);
            _mouseDownPos = pointerPos;
            return true;
        }

        /// <summary>
        /// 掴んだ側のカメラ基準でポインタ位置へ点を移動する。
        /// ロールモードでは点は動かさず、横方向の総移動量で上腕 / 手を骨の軸まわりに回す
        /// </summary>
        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || _dragCamera == null)
            {
                return;
            }

            if (_isRollMode)
            {
                // フレーム差分の積算だと誤差が溜まるため、押下位置からの総移動量で決める
                var angle = (pointerPos.x - _mouseDownPos.x) / RollDivisor;
                followBone.localRotation = _rollBaseRotation * Quaternion.AngleAxis(angle, Vector3.right);
                return;
            }

            var pos = new Vector3(pointerPos.x, pointerPos.y, _screenPoint.z);
            transform.position = _dragCamera.ScreenToWorldPoint(pos) + _offset;
        }

        public void EndDrag(Vector3 pointerPos)
        {
            if (!_isDragging)
            {
                return;
            }

            var downPos = _mouseDownPos;
            var isClick = (pointerPos - downPos).magnitude <= ClickThresholdPixels;
            // クリック (微小移動) のロールは戻す。1px で約 0.7 度回るため、
            // Ctrl のクリックで Inspector を開くたびに上腕 / 手が少しずつ回ってしまう
            if (isClick && _isRollMode)
            {
                followBone.localRotation = _rollBaseRotation;
            }
            CancelDrag();
            MaidDragBoneTracker.NotifyDragCompleted(maid);

            // 選択自体は BeginDrag 済み。クリック（微小移動）なら Inspector も開く
            if (!isClick)
            {
                _lastClickTime = NoClickTime;
                return;
            }

            InspectorWindow.instance.isShowWnd = true;

            var now = Time.realtimeSinceStartup;
            if (_lastClickTime >= 0f && now - _lastClickTime < DoubleClickTime)
            {
                // 成立直後の 3 回目のクリックで再トグルさせないよう判定状態を戻す
                _lastClickTime = NoClickTime;
                ToggleHold();
                return;
            }
            _lastClickTime = now;
        }

        /// <summary>
        /// ダブルクリックで IK 固定を切り替える。Inspector のトグルと同じ経路で履歴に残す。
        /// 肩・胸には対応する固定タイプが無いため何もしない
        /// </summary>
        private void ToggleHold()
        {
            MaidIKHoldType holdType;
            if (!MaidIKHoldController.TryGetHoldType(followBone.name, out holdType))
            {
                return;
            }

            var holdController = MaidManipulateManager.instance.ikHoldController;
            var hold = !holdController.GetHold(maid, holdType);
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.IK,
                "IK固定: " + MaidIKHoldController.GetHoldTypeName(holdType));
            holdController.SetHold(maid, holdType, hold);
            MaidDragBoneTracker.NotifyBoneEdited(maid);
        }

        /// <summary>
        /// クリック判定を伴わないドラッグ終了。SceneView の非表示など、
        /// ポインタ位置が意味を持たない形で入力経路が絶たれたときの後始末に使う
        /// </summary>
        public void CancelDrag()
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            _dragCamera = null;
            _isRollMode = false;
            MaidDragBoneTracker.EndDrag();
            if (chain != null)
            {
                chain.EndDrag();
            }
        }

        private void OnMouseDown()
        {
            // ゲーム画面で見えていない白丸は掴ませない
            if (!MaidManipulateManager.instance.isGameViewDragPointVisible)
            {
                return;
            }
            BeginDrag(GetCamera(), Input.mousePosition);
        }

        // SceneView 発のドラッグをゲーム画面側のマウスメッセージで動かさないよう掴んだ側を確認する
        private void OnMouseDrag()
        {
            if (_dragCamera != GetCamera())
            {
                return;
            }

            UpdateDrag(Input.mousePosition);
        }

        private void OnMouseUp()
        {
            if (_dragCamera != GetCamera())
            {
                return;
            }

            EndDrag(Input.mousePosition);
        }

        /// <summary>Inspector に表示する IK 点の名前</summary>
        public string displayName
        {
            get { return sliderBoneName ?? (followBone != null ? followBone.name : name); }
        }

        /// <summary>
        /// Inspector で編集する IK 座標。IK が解く対象ボーン（手首/足首/肘/膝/胸先端）の
        /// ワールド位置。胸のドラッグ点自体は中点に置かれるが、編集値と解いた結果を
        /// 一致させるため点の位置ではなくボーン位置を使う
        /// </summary>
        public Vector3 targetPosition
        {
            get { return followBone != null ? followBone.position : transform.position; }
        }

        /// <summary>Inspector から編集を受け付けられる状態か（対象ボーン欠落時の表示切替用）</summary>
        public bool canEdit
        {
            get { return IsReady(); }
        }

        /// <summary>
        /// Inspector からの数値編集で IK 座標を適用する。
        /// ドラッグと同じ前処理を行い、1 回だけ解く
        /// </summary>
        public void ApplyTargetPosition(Vector3 position)
        {
            if (!IsReady())
            {
                return;
            }

            PrepareEdit("IK座標編集: ");
            chain.Solve(pointType, position);

            // 編集した箇所が IK 固定中だと、固定側の LateUpdate が旧固定位置へ解き直して
            // 編集結果を上書きしてしまう。Undo 復元と同じく固定ターゲットを現在位置へ取り直す
            MaidManipulateManager.instance.ikHoldController.ResetAllTargetPositions(maid);
        }

        /// <summary>
        /// 編集開始の共通前処理（ドラッグ・Inspector 編集で共有）。
        /// モーション停止 → 履歴記録 → 胸の揺れ物停止の順序を 1 箇所に揃える
        /// </summary>
        private void PrepareEdit(string historyLabelPrefix)
        {
            MaidMotionState.StopMotion(maid);

            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                historyLabelPrefix + displayName, _isRollMode ? GetRollHistoryBones() : chain.bones);

            if (isMune)
            {
                // 揺れたままだと LateUpdate で手付けが上書きされる。
                // 編集モードを抜けても戻さない（戻すのはトグル操作・Undo・anm 読み込みのみ）。
                // BeforeEdit より後に呼ぶのは、変更前の揺れ状態を履歴へ残すため
                MaidManipulateManager.instance.muneYureController
                    .SetYure(maid, isMuneLeft, false);
            }
        }

        /// <summary>
        /// ロールの履歴対象。肩のロールで手首を IK 固定していると、固定側が手首を引き戻して
        /// 前腕・手も書き換えるため含める
        /// </summary>
        private List<Transform> GetRollHistoryBones()
        {
            var bones = new List<Transform>(chain.bones);
            foreach (var name in new[] { "Forearm", "Hand" })
            {
                var bone = CMT.SearchObjName(followBone, followBone.name.Replace("UpperArm", name), false);
                if (bone != null && !bones.Contains(bone))
                {
                    bones.Add(bone);
                }
            }
            return bones;
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
            // ドラッグ中は骨へ戻さない。戻すと target が骨に引き寄せられて動かせなくなる
            // （ゲーム側 IKDragPoint.Update も非ドラッグ時のみ追従させている）
            if (_isDragging || followBone == null)
            {
                return;
            }

            transform.position = followBoneSub != null
                ? (followBone.position + followBoneSub.position) / 2f
                : followBone.position;
        }
    }
}
