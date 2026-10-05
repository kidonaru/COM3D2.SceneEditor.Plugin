using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ノード表示のスナップショット (SE の上書き一式)</summary>
    public class NodeVisibilitySnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, bool> _overrides;

        public static NodeVisibilitySnapshot Capture(Maid maid)
        {
            return new NodeVisibilitySnapshot
            {
                _capturedMaid = maid,
                _overrides = MaidNodeVisibilityController.GetOverrides(maid),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            MaidNodeVisibilityController.SetOverrides(maid, _overrides);
            MaidNodeVisibilityController.Flush(maid);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as NodeVisibilitySnapshot;
            if (o == null || o._overrides.Count != _overrides.Count)
            {
                return false;
            }
            foreach (var pair in _overrides)
            {
                bool value;
                if (!o._overrides.TryGetValue(pair.Key, out value) || value != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
