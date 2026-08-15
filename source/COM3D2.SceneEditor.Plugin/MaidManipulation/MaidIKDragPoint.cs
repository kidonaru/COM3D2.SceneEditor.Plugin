using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// IK のドラッグ点。手首/足首・肘/膝・胸に置く。
    /// 透明な球コライダを骨に追従させ、掴んでいる間は自分自身が FABRIK の target になる。
    /// 解くのは MaidIKChain 側で、この点はマウス位置をワールド座標へ変換して置くだけ
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

        /// <summary>これ以下の移動量ならドラッグではなくクリックとみなす (px)</summary>
        private const float ClickThresholdPixels = 5f;

        private bool _isDragging = false;
        private Vector3 _screenPoint;
        private Vector3 _offset;
        private Vector3 _mouseDownPos;

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

        /// <summary>ボーンギズモを掴む操作と取り合いにならないよう、Alt 中は IK ドラッグしない</summary>
        public bool canDrag
        {
            get { return !IsAltHeld(); }
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

        /// <summary>Ctrl 押下中は肘/膝を固定する（MM の ikMode==2、ゲーム側の joint_lock と同じ割り当て）</summary>
        private static bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>
        /// ドラッグ開始。掴めたら true。
        /// pointerPos は camera の描画面上の座標（ゲーム画面は Input.mousePosition、
        /// SceneView は RT 座標）で、ゲーム画面と SceneView の両方から呼ばれる
        /// </summary>
        public bool BeginDrag(Camera camera, Vector3 pointerPos)
        {
            if (_isDragging || !IsReady() || camera == null || !canDrag)
            {
                return false;
            }

            _dragCamera = camera;
            _screenPoint = camera.WorldToScreenPoint(transform.position);
            _offset = transform.position - camera.ScreenToWorldPoint(
                new Vector3(pointerPos.x, pointerPos.y, _screenPoint.z));

            PrepareEdit("IK操作: ");

            // 掴んだ時点で IK 選択に切り替える。クリック確定 (EndDrag) まで待つと、
            // ボーン選択が生きたままドラッグが始まり Inspector のボーン自動追従に奪われる
            SelectionManager.instance.SelectIK(this);

            // 固定するかは掴んだ時点で決める。途中で Ctrl を離してもモードは変えない
            chain.BeginDrag(pointType, IsCtrlHeld(), transform);
            _isDragging = true;
            MaidDragBoneTracker.BeginDrag(sliderBoneName ?? followBone.name);
            _mouseDownPos = pointerPos;
            return true;
        }

        /// <summary>掴んだ側のカメラ基準でポインタ位置へ点を移動する</summary>
        public void UpdateDrag(Vector3 pointerPos)
        {
            if (!_isDragging || _dragCamera == null)
            {
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
            CancelDrag();

            // 選択自体は BeginDrag 済み。クリック（微小移動）なら Inspector も開く
            if ((pointerPos - downPos).magnitude <= ClickThresholdPixels)
            {
                InspectorWindow.instance.isShowWnd = true;
            }
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
            MaidDragBoneTracker.EndDrag();
            if (chain != null)
            {
                chain.EndDrag();
            }
        }

        private void OnMouseDown()
        {
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
                historyLabelPrefix + displayName, chain.bones);

            if (isMune)
            {
                // 揺れたままだと LateUpdate で手付けが上書きされる。
                // 編集モードを抜けても戻さない（戻すのはトグル操作・Undo・anm 読み込みのみ）。
                // BeforeEdit より後に呼ぶのは、変更前の揺れ状態を履歴へ残すため
                MaidManipulateManager.instance.muneYureController
                    .SetYure(maid, isMuneLeft, false);
            }
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
