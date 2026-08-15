using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>脱衣のスナップショット (スロットマスクとめくれ系)</summary>
    public class UndressSnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;
        private List<string> _undressedSlots;
        private List<string> _costumeTypes;

        public static UndressSnapshot Capture(Maid maid)
        {
            return new UndressSnapshot
            {
                _capturedMaid = maid,
                _undressedSlots = MaidUndressController.CaptureUndressedSlots(maid),
                _costumeTypes = MaidUndressController.CaptureCostumeTypes(maid),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            MaidUndressController.ApplyUndressedSlots(maid, _undressedSlots);
            MaidUndressController.ApplyCostumeTypes(maid, _costumeTypes);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as UndressSnapshot;
            return o != null
                && ListEquals(_undressedSlots, o._undressedSlots)
                && ListEquals(_costumeTypes, o._costumeTypes);
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);

        private static bool ListEquals(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
