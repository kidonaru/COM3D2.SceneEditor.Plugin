using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドスケールのスナップショット (骨ごとの倍率)</summary>
    public class MaidScaleSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        /// <summary>MaidScaleBones.bones と同じ並び</summary>
        private float[] _scales;

        private static MaidScaleController controller
            => MaidManipulateManager.instance.maidScaleController;

        public static MaidScaleSnapshot Capture(Maid maid)
        {
            var bones = MaidScaleBones.bones;
            var scales = new float[bones.Count];
            for (var i = 0; i < bones.Count; i++)
            {
                scales[i] = controller.GetScale(maid, bones[i].boneName);
            }

            return new MaidScaleSnapshot
            {
                _capturedMaid = maid,
                _scales = scales,
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            var bones = MaidScaleBones.bones;
            for (var i = 0; i < bones.Count && i < _scales.Length; i++)
            {
                controller.SetScale(maid, bones[i].boneName, _scales[i]);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaidScaleSnapshot;
            if (o == null || o._scales.Length != _scales.Length)
            {
                return false;
            }
            for (var i = 0; i < _scales.Length; i++)
            {
                if (!Mathf.Approximately(_scales[i], o._scales[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
