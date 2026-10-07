using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影のスナップショット (SE の上書き一式)</summary>
    public class FaceShadowSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        private Dictionary<string, bool> _overrides;

        public static FaceShadowSnapshot Capture(Maid maid)
        {
            return new FaceShadowSnapshot
            {
                _capturedMaid = maid,
                _overrides = MaidFaceRendererManager.instance.GetOverrides(maid),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            MaidFaceRendererManager.instance.SetOverrides(maid, _overrides);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as FaceShadowSnapshot;
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
