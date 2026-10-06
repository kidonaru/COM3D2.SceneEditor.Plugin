using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>体型スライダーのスナップショット (既定値でない項目の値)</summary>
    public class BodySliderSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, Vector3> _values;

        private static BodySliderController controller
            => MaidManipulateManager.instance.bodySliderController;

        public static BodySliderSnapshot Capture(Maid maid)
        {
            var values = new Dictionary<string, Vector3>();
            foreach (var pair in controller.GetNonDefaultValues(maid))
            {
                values[pair.Key] = pair.Value;
            }

            return new BodySliderSnapshot
            {
                _capturedMaid = maid,
                _values = values,
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        /// <summary>記録の無い項目は既定値へ戻す</summary>
        public void Apply(Maid maid)
        {
            foreach (var item in BodySliderDefs.items)
            {
                Vector3 values;
                controller.SetValues(maid, item.key,
                    _values.TryGetValue(item.key, out values) ? values : item.defaultValues);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as BodySliderSnapshot;
            if (o == null || o._values.Count != _values.Count)
            {
                return false;
            }
            foreach (var pair in _values)
            {
                Vector3 otherValues;
                if (!o._values.TryGetValue(pair.Key, out otherValues) || pair.Value != otherValues)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
