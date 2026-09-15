using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルのシェイプキー 1 件のスナップショット (重みと追跡チェック)。
    /// ModelBlendShape は名前でモデルから引き直す (モデル差し替えで実体が変わるため)
    /// </summary>
    public class ModelShapeKeySnapshot : IStateSnapshot
    {
        private MTEP.StudioModelStat _model;
        private string _shapeKeyName;
        private float _weight;
        private bool _isTracked;

        public static ModelShapeKeySnapshot Capture(MTEP.StudioModelStat model, string shapeKeyName)
        {
            var blendShape = FindBlendShape(model, shapeKeyName);
            var store = ModelShapeKeyEditManager.instance.FindStore(model.transform.gameObject);
            return new ModelShapeKeySnapshot
            {
                _model = model,
                _shapeKeyName = shapeKeyName,
                _weight = blendShape != null ? blendShape.weight : 0f,
                _isTracked = store != null && store.IsModified(shapeKeyName),
            };
        }

        private static MTEP.ModelBlendShape FindBlendShape(MTEP.StudioModelStat model, string shapeKeyName)
        {
            if (model == null || model.transform == null || model.blendShapes == null)
            {
                return null;
            }
            foreach (var blendShape in model.blendShapes)
            {
                if (blendShape.shapeKeyName == shapeKeyName)
                {
                    return blendShape;
                }
            }
            return null;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_model, _shapeKeyName);

        public void Apply(Maid maid)
        {
            var blendShape = FindBlendShape(_model, _shapeKeyName);
            if (blendShape == null)
            {
                return;
            }
            blendShape.weight = _weight;
            _model.FixBlendValues();

            var store = ModelShapeKeyEditManager.instance.GetStore(_model.transform.gameObject);
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
            var o = other as ModelShapeKeySnapshot;
            return o != null && o._model == _model && o._shapeKeyName == _shapeKeyName
                && o._isTracked == _isTracked && Mathf.Approximately(o._weight, _weight);
        }

        public bool CanApply(Maid maid) => FindBlendShape(_model, _shapeKeyName) != null;
    }
}
