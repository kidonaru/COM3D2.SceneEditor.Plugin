using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表情のスナップショット (全モーフ値とまばたき)</summary>
    public class FaceSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;
        private Dictionary<FaceMorphDef, float> _morphs;
        private bool _mabataki;

        public static FaceSnapshot Capture(Maid maid)
        {
            var snapshot = new FaceSnapshot
            {
                _capturedMaid = maid,
                _morphs = new Dictionary<FaceMorphDef, float>(),
            };

            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    snapshot._morphs[def] = MaidFaceMorphController.GetMorphValue(maid, def);
                }
            }
            snapshot._mabataki = MaidFaceMorphController.GetMabataki(maid);
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            // まばたき有効のまま書き戻すと毎フレーム上書きされるため先に反映する
            MaidFaceMorphController.SetMabataki(maid, _mabataki);
            foreach (var pair in _morphs)
            {
                MaidFaceMorphController.SetMorphValue(maid, pair.Key, pair.Value);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as FaceSnapshot;
            if (o == null || _mabataki != o._mabataki || _morphs.Count != o._morphs.Count)
            {
                return false;
            }

            foreach (var pair in _morphs)
            {
                float otherValue;
                if (!o._morphs.TryGetValue(pair.Key, out otherValue)
                    || Mathf.Abs(pair.Value - otherValue) >= 0.001f)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);
    }
}
