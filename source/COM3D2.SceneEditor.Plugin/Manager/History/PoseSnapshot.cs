using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポーズのスナップショット。操作が触るボーンだけを対象にし、
    /// 全骨格の丸ごと保存はしない
    /// </summary>
    public class PoseSnapshot : IStateSnapshot
    {
        private readonly BoneTrsMap _bones = new BoneTrsMap();
        private Quaternion _eyeL;
        private Quaternion _eyeR;
        private bool _headToCam;
        private bool _eyeToCam;

        /// <summary>記録時の視線の向け先。ボーンが動かない切替も履歴に残すために持つ</summary>
        private MaidLookMode _lookMode;
        private float _lookX;
        private float _lookY;
        private Transform _lookTarget;

        /// <summary>記録時の胸の揺れもの状態。ボーンが動かないトグル操作も履歴に残すために持つ</summary>
        private bool _muneYureL;
        private bool _muneYureR;

        /// <summary>Capture したメイド。CaptureCurrent で body の値を取り直すのに使う</summary>
        private Maid _capturedMaid;

        /// <summary>記録時のモーション再生状態</summary>
        private string _clipName;
        private bool _isPlaying;
        private float _playbackTime;

        /// <summary>記録時の指ブレンド状態。コントローラの対象が一致するときだけ復元する</summary>
        private List<FingerUnitState> _fingerStates;

        /// <summary>記録時のボーン編集ストア。スロットリセットの undo で記録を復活させる</summary>
        private List<BoneEditEntry> _boneEditEntries;

        public static PoseSnapshot Capture(Maid maid, IEnumerable<Transform> targetBones)
        {
            var snapshot = new PoseSnapshot { _capturedMaid = maid };
            snapshot.AddBones(targetBones);

            var body = maid != null ? maid.body0 : null;
            if (body != null)
            {
                snapshot._eyeL = body.quaDefEyeL;
                snapshot._eyeR = body.quaDefEyeR;
                snapshot._headToCam = body.boHeadToCam;
                snapshot._eyeToCam = body.boEyeToCam;
            }

            var lookController = MaidManipulateManager.instance.lookController;
            snapshot._lookMode = lookController.GetMode(maid);
            snapshot._lookX = lookController.GetLookX(maid);
            snapshot._lookY = lookController.GetLookY(maid);
            snapshot._lookTarget = lookController.GetTarget(maid);

            snapshot._clipName = MaidMotionState.GetCurrentClipName(maid);
            snapshot._isPlaying = MaidMotionState.IsPlaying(maid);
            var animState = MaidMotionState.GetCurrentAnimationState(maid);
            snapshot._playbackTime = animState != null
                ? MaidMotionState.GetWrappedTime(animState) : 0f;

            var muneYureController = MaidManipulateManager.instance.muneYureController;
            snapshot._muneYureL = muneYureController.GetYure(maid, true);
            snapshot._muneYureR = muneYureController.GetYure(maid, false);

            snapshot.CaptureFingerBlend(maid);

            var store = BoneEditManager.instance.FindStore(maid);
            snapshot._boneEditEntries = store != null ? store.CaptureEntries() : null;

            return snapshot;
        }

        /// <summary>
        /// 指ブレンドの開き/握り/ロック状態を控える。
        /// コントローラは単一インスタンスで対象を差し替えるため、対象が一致するときだけ記録する
        /// </summary>
        private void CaptureFingerBlend(Maid maid)
        {
            var controller = MaidManipulateManager.instance.fingerBlendController;
            if (controller.maid != maid)
            {
                return;
            }

            _fingerStates = new List<FingerUnitState>();
            foreach (var type in MaidFingerPresetManager.BlendTypes)
            {
                var unit = controller.GetUnit(type);
                if (unit != null)
                {
                    _fingerStates.Add(unit.CaptureState());
                }
            }
        }

        /// <summary>
        /// 指ブレンドの表示値を戻す。ボーンは TRS 側で復元済みなので Apply は呼ばない
        /// (呼ぶとブレンド値から再計算されて復元結果とずれる)
        /// </summary>
        private void RestoreFingerBlend(Maid maid)
        {
            if (_fingerStates == null)
            {
                return;
            }

            var controller = MaidManipulateManager.instance.fingerBlendController;
            // 対象が切り替わっている場合、値だけ戻すと別メイドへ適用されるため触らない
            if (controller.maid != maid)
            {
                return;
            }

            foreach (var state in _fingerStates)
            {
                var unit = controller.GetUnit(state.type);
                if (unit != null)
                {
                    unit.RestoreState(state);
                }
            }
        }

        /// <summary>
        /// 記録時のモーション再生状態へ戻す。
        /// 停止中でも再開先クリップを揃え、再生ボタンが古いクリップを掴まないようにする
        /// </summary>
        private void RestoreMotion(Maid maid)
        {
            if (_isPlaying)
            {
                MaidMotionState.PlayClip(maid, _clipName, _playbackTime);
                return;
            }
            MaidMotionState.SetResumeClip(maid, _clipName);
        }

        /// <summary>
        /// 全身のボーン。モーション適用など骨格全体を書き換える操作の記録対象に使う
        /// </summary>
        public static IEnumerable<Transform> GetAllBodyBones(Maid maid)
        {
            if (maid == null || maid.body0 == null || maid.body0.m_Bones == null)
            {
                return new Transform[0];
            }
            return maid.body0.m_Bones.transform.GetComponentsInChildren<Transform>(true);
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
            _bones.AddBones(targetBones);
        }

        public IStateSnapshot CaptureCurrent()
        {
            // 記録済みの対象集合について、現在の値で捕捉し直す
            return Capture(_capturedMaid, _bones.targets);
        }

        public void Apply(Maid maid)
        {
            // モーション再生中に書き戻しても次フレームで上書きされるため停止する
            MaidMotionState.StopMotion(maid);

            // 揺れものはボーンより先に戻す。揺れたままボーンを書き戻すと上書きされる
            var muneYureController = MaidManipulateManager.instance.muneYureController;
            muneYureController.SetYure(maid, true, _muneYureL);
            muneYureController.SetYure(maid, false, _muneYureR);

            _bones.Apply();

            var body = maid != null ? maid.body0 : null;
            if (body != null)
            {
                body.quaDefEyeL = _eyeL;
                body.quaDefEyeR = _eyeR;
                body.boHeadToCam = _headToCam;
                body.boEyeToCam = _eyeToCam;
            }

            MaidManipulateManager.instance.lookController.SetState(
                maid, _lookMode, _lookX, _lookY, _lookTarget);

            // IK 固定が復元前の位置へ解き直すと undo が打ち消されるため、
            // 復元後のボーン位置を新しい固定ターゲットとして取り直させる
            MaidManipulateManager.instance.ikHoldController.ResetAllTargetPositions(maid);

            // StopMotion は再生中のポーズを基準として控えるため、書き戻し後は食い違う。
            // 捨てておけばボーンスライダーが次に必要としたときに復元後の値で取り直す
            MaidBoneSliderController.ClearBasePose(maid);

            RestoreBoneEditStore(maid);
            RestoreFingerBlend(maid);

            // 再生の復元は最後。ボーンを書き戻す前に再生するとポーズが上書きされる
            RestoreMotion(maid);
        }

        /// <summary>
        /// ボーン編集ストアを記録時の内容へ戻す。
        /// Transform を書き戻すだけではリセットで消えた記録が復活せず、
        /// 着替え後の再適用 (ReapplySlot) で編集が失われる
        /// </summary>
        private void RestoreBoneEditStore(Maid maid)
        {
            var store = BoneEditManager.instance.FindStore(maid);
            if (store != null)
            {
                store.RestoreEntries(_boneEditEntries);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as PoseSnapshot;
            if (o == null)
            {
                return false;
            }

            if (Quaternion.Angle(_eyeL, o._eyeL) >= 0.01f
                || Quaternion.Angle(_eyeR, o._eyeR) >= 0.01f
                || _headToCam != o._headToCam
                || _eyeToCam != o._eyeToCam
                || _lookMode != o._lookMode
                || Mathf.Abs(_lookX - o._lookX) >= 0.001f
                || Mathf.Abs(_lookY - o._lookY) >= 0.001f
                || _lookTarget != o._lookTarget
                || _muneYureL != o._muneYureL
                || _muneYureR != o._muneYureR)
            {
                return false;
            }

            // 再生位置は比較しない。再生中は毎フレーム進むため、含めると
            // 実質的な変更のない操作まで差分ありと判定してしまう
            if (_isPlaying != o._isPlaying || _clipName != o._clipName)
            {
                return false;
            }
            if (!FingerStatesApproximately(o))
            {
                return false;
            }
            if (!BoneEditEntriesApproximately(o))
            {
                return false;
            }
            return _bones.Approximately(o._bones);
        }

        /// <summary>
        /// ボーン編集ストアの記録対象が一致するか。値は _bones 側で比較されるため
        /// ここでは (スロット, ボーン) の集合だけ見る。ボーンが動かないリセット
        /// (編集値が元の値と一致していた場合) も履歴に残せるようにする
        /// </summary>
        private bool BoneEditEntriesApproximately(PoseSnapshot other)
        {
            if (_boneEditEntries == null || other._boneEditEntries == null)
            {
                return _boneEditEntries == null && other._boneEditEntries == null;
            }
            if (_boneEditEntries.Count != other._boneEditEntries.Count)
            {
                return false;
            }

            for (var i = 0; i < _boneEditEntries.Count; i++)
            {
                var a = _boneEditEntries[i];
                var b = other._boneEditEntries[i];
                if (a.slotName != b.slotName || a.boneName != b.boneName)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 指ブレンドの表示値が一致するか。
        /// ボーンが動かないロック切替だけの操作も履歴に残せるようにする
        /// </summary>
        private bool FingerStatesApproximately(PoseSnapshot other)
        {
            if (_fingerStates == null || other._fingerStates == null)
            {
                return _fingerStates == null && other._fingerStates == null;
            }
            if (_fingerStates.Count != other._fingerStates.Count)
            {
                return false;
            }

            for (var i = 0; i < _fingerStates.Count; i++)
            {
                var a = _fingerStates[i];
                var b = other._fingerStates[i];
                if (a.type != b.type
                    || Mathf.Abs(a.valueOpen - b.valueOpen) >= 0.001f
                    || Mathf.Abs(a.valueFist - b.valueFist) >= 0.001f
                    || a.digits.Count != b.digits.Count)
                {
                    return false;
                }
                for (var j = 0; j < a.digits.Count; j++)
                {
                    if (a.digits[j].isLock != b.digits[j].isLock)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
