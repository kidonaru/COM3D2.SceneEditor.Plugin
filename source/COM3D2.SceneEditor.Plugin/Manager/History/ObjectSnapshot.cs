using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>一般オブジェクトのスナップショット (Transform)</summary>
    public class ObjectSnapshot : IStateSnapshot
    {
        private readonly BoneTrsMap _bones = new BoneTrsMap();

        /// <summary>対象の表示状態。Inspector のアクティブトグルを戻すのに使う</summary>
        private readonly Dictionary<Transform, bool> _actives = new Dictionary<Transform, bool>();

        public static ObjectSnapshot Capture(IEnumerable<Transform> targets)
        {
            var snapshot = new ObjectSnapshot();
            snapshot.AddBones(targets);
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
            _bones.AddBones(targetBones);

            if (targetBones == null)
            {
                return;
            }

            foreach (var target in targetBones)
            {
                if (target != null && !_actives.ContainsKey(target))
                {
                    _actives[target] = target.gameObject.activeSelf;
                }
            }
        }

        public IStateSnapshot CaptureCurrent()
        {
            return Capture(_bones.targets);
        }

        public void Apply(Maid maid)
        {
            _bones.Apply();

            foreach (var pair in _actives)
            {
                if (pair.Key != null)
                {
                    pair.Key.gameObject.SetActive(pair.Value);
                }
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as ObjectSnapshot;
            if (o == null || !_bones.Approximately(o._bones))
            {
                return false;
            }

            foreach (var pair in _actives)
            {
                bool otherActive;
                if (!o._actives.TryGetValue(pair.Key, out otherActive)
                    || pair.Value != otherActive)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid)
        {
            // 対象 Transform が全滅していなければ適用できる
            return _bones.hasAliveBones;
        }
    }
}
