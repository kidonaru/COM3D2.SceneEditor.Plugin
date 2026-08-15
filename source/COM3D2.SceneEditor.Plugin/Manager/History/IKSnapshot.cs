using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// IK 固定のスナップショット (固定フラグと接地パラメータ)。
    /// パラメータの入れ物はシーンプリセットの型を流用する
    /// </summary>
    public class IKSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;
        private readonly bool[] _holds = new bool[(int)MaidIKHoldType.Max];
        private ScenePresetIKParams _params;

        public static IKSnapshot Capture(Maid maid)
        {
            var controller = MaidManipulateManager.instance.ikHoldController;
            var snapshot = new IKSnapshot { _capturedMaid = maid };

            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                snapshot._holds[i] = controller.GetHold(maid, (MaidIKHoldType)i);
            }

            var holdParams = controller.GetParamsOrNull(maid) ?? MaidIKHoldParams.Default;
            snapshot._params = ScenePresetIKParams.FromParams(holdParams);
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            var controller = MaidManipulateManager.instance.ikHoldController;

            // 接地判定に使うため、パラメータを固定 ON より先に反映する
            // (ScenePresetManager.ApplyIKHold と同じ順序)
            _params.ApplyTo(controller.GetParams(maid));

            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                controller.SetHold(maid, (MaidIKHoldType)i, _holds[i]);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as IKSnapshot;
            if (o == null)
            {
                return false;
            }

            for (var i = 0; i < _holds.Length; i++)
            {
                if (_holds[i] != o._holds[i])
                {
                    return false;
                }
            }

            return _params.isGroundingFootL == o._params.isGroundingFootL
                && _params.isGroundingFootR == o._params.isGroundingFootR
                && Mathf.Approximately(_params.floorHeight, o._params.floorHeight)
                && Mathf.Approximately(_params.footBaseOffset, o._params.footBaseOffset)
                && Mathf.Approximately(_params.footStretchHeight, o._params.footStretchHeight)
                && Mathf.Approximately(_params.footStretchAngle, o._params.footStretchAngle)
                && Mathf.Approximately(_params.footGroundAngle, o._params.footGroundAngle);
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
