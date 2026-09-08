using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのシェイプキー 1 件のスナップショット (重みと追跡チェック)。
    /// MaidBlendShape は着替えでキャッシュが作り直されるため参照を持たず、名前で引き直す
    /// </summary>
    public class MaidShapeKeySnapshot : IStateSnapshot
    {
        private Maid _maid;
        private MTEP.MaidCache _maidCache;
        private string _shapeKeyName;
        private float _weight;
        private bool _isTracked;

        public static MaidShapeKeySnapshot Capture(
            Maid maid, MTEP.MaidCache maidCache, string shapeKeyName)
        {
            var store = MaidShapeKeyEditManager.instance.FindStore(maid);
            return new MaidShapeKeySnapshot
            {
                _maid = maid,
                _maidCache = maidCache,
                _shapeKeyName = shapeKeyName,
                _weight = maidCache.GetBlendShapeValue(shapeKeyName),
                _isTracked = store != null && store.IsModified(shapeKeyName),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_maid, _maidCache, _shapeKeyName);

        public void Apply(Maid maid)
        {
            _maidCache.SetBlendShapeValue(_shapeKeyName, _weight);
            _maidCache.FixBlendValues(new[] { _shapeKeyName });

            var store = MaidShapeKeyEditManager.instance.GetStore(_maid);
            if (_isTracked)
            {
                store.Mark(_shapeKeyName);
            }
            else
            {
                store.Unmark(_shapeKeyName);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaidShapeKeySnapshot;
            return o != null && o._maid == _maid && o._shapeKeyName == _shapeKeyName
                && o._isTracked == _isTracked && Mathf.Approximately(o._weight, _weight);
        }

        /// <summary>着替え中や該当 morph を失ったスロット構成では適用しない</summary>
        public bool CanApply(Maid maid)
        {
            return HistoryScopeUtils.CanEditMaid(_maid)
                && MaidShapeKeyRowDrawer.IsEditable(_maidCache.GetBlendShape(_shapeKeyName));
        }
    }
}
