using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>重力のスナップショット (カテゴリごとの有効フラグとオフセット)</summary>
    public class GravitySnapshot : IStateSnapshot
    {
        private Maid _capturedMaid;

        /// <summary>MaidGravityController.categories と同じ並び</summary>
        private bool[] _enabled;
        private Vector3[] _offsets;

        public static GravitySnapshot Capture(Maid maid)
        {
            var categories = MaidGravityController.categories;
            var enabled = new bool[categories.Count];
            var offsets = new Vector3[categories.Count];

            var controller = MaidManipulateManager.instance.gravityController;
            for (var i = 0; i < categories.Count; i++)
            {
                enabled[i] = controller.GetEnabled(maid, categories[i]);
                offsets[i] = controller.GetOffset(maid, categories[i]);
            }

            return new GravitySnapshot
            {
                _capturedMaid = maid,
                _enabled = enabled,
                _offsets = offsets,
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_capturedMaid);

        public void Apply(Maid maid)
        {
            var categories = MaidGravityController.categories;
            var controller = MaidManipulateManager.instance.gravityController;

            // 重力を一度も使っていないメイドへ既定値だけを書き戻すと、
            // 何も変わらないのにコンポーネントだけが作られて常駐コストになる
            // (ScenePresetManager.ApplyGravity と同じ判定)
            if (!controller.HasState(maid) && IsAllDefault())
            {
                return;
            }

            for (var i = 0; i < categories.Count && i < _enabled.Length; i++)
            {
                controller.SetOffset(maid, categories[i], _offsets[i]);
                controller.SetEnabled(maid, categories[i], _enabled[i]);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as GravitySnapshot;
            if (o == null || o._enabled.Length != _enabled.Length)
            {
                return false;
            }
            for (var i = 0; i < _enabled.Length; i++)
            {
                if (_enabled[i] != o._enabled[i])
                {
                    return false;
                }
                if (!Mathf.Approximately(_offsets[i].x, o._offsets[i].x)
                    || !Mathf.Approximately(_offsets[i].y, o._offsets[i].y)
                    || !Mathf.Approximately(_offsets[i].z, o._offsets[i].z))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => HistoryScopeUtils.CanEditMaid(maid);

        /// <summary>全カテゴリが既定値（無効・オフセット 0）か</summary>
        private bool IsAllDefault()
        {
            for (var i = 0; i < _enabled.Length; i++)
            {
                if (_enabled[i] || _offsets[i] != Vector3.zero)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
