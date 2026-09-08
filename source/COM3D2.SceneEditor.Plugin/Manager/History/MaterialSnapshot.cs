using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル 1 件のスナップショット (全色・数値プロパティと追跡チェック)。
    /// 差分ではなく全プロパティを持つので、初期化ボタンやペーストのように
    /// 複数プロパティが一度に変わる操作も 1 エントリで戻せる
    /// </summary>
    public class MaterialSnapshot : IStateSnapshot
    {
        private MTEP.ModelMaterial _material;
        private MaterialTrackTarget _track;
        private string _trackKey;

        private readonly Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color> _colors
            = new Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color>();
        private readonly Dictionary<MTEP.ModelMaterial.ValuePropertyType, float> _values
            = new Dictionary<MTEP.ModelMaterial.ValuePropertyType, float>();
        private bool _isTracked;

        /// <param name="trackKey">追跡チェックの記録名。追跡しない対象 (背景タブ) は null</param>
        public static MaterialSnapshot Capture(
            MTEP.ModelMaterial material, MaterialTrackTarget track, string trackKey)
        {
            var snapshot = new MaterialSnapshot
            {
                _material = material,
                _track = track,
                _trackKey = trackKey,
            };

            foreach (var type in MTEP.ModelMaterial.ColorPropertyTypes)
            {
                if (material.HasColor(type))
                {
                    snapshot._colors[type] = material.GetColor(type);
                }
            }
            foreach (var type in MTEP.ModelMaterial.ValuePropertyTypes)
            {
                if (material.HasValue(type))
                {
                    snapshot._values[type] = material.GetValue(type);
                }
            }

            if (trackKey != null)
            {
                var store = track.findStore();
                snapshot._isTracked = store != null && store.IsModified(trackKey);
            }
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_material, _track, _trackKey);

        public void Apply(Maid maid)
        {
            foreach (var pair in _colors)
            {
                if (_material.HasColor(pair.Key))
                {
                    _material.SetColor(pair.Key, pair.Value);
                }
            }
            foreach (var pair in _values)
            {
                if (_material.HasValue(pair.Key))
                {
                    _material.SetValue(pair.Key, pair.Value);
                }
            }

            if (_trackKey != null)
            {
                // 追跡 OFF へ戻すときもストアを作る (Unmark だけなら空ストアが残るが害はない)
                var store = _track.getStore();
                if (_isTracked)
                {
                    store.Mark(_trackKey);
                }
                else
                {
                    store.Unmark(_trackKey);
                }
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaterialSnapshot;
            if (o == null || o._material != _material || o._isTracked != _isTracked
                || o._colors.Count != _colors.Count || o._values.Count != _values.Count)
            {
                return false;
            }
            foreach (var pair in _colors)
            {
                Color color;
                if (!o._colors.TryGetValue(pair.Key, out color) || color != pair.Value)
                {
                    return false;
                }
            }
            foreach (var pair in _values)
            {
                float value;
                if (!o._values.TryGetValue(pair.Key, out value)
                    || !Mathf.Approximately(value, pair.Value))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 着替え・モデル削除で Unity 側の Material が破棄されたら適用しない。
        /// 着替え後に「更新」を押すまで古い ModelMaterial が一覧に残る場合は、
        /// 破棄済みでない限り旧マテリアルへ書き戻す (見た目に反映されないが害はない)
        /// </summary>
        public bool CanApply(Maid maid) => _material != null && _material.material != null;
    }
}
