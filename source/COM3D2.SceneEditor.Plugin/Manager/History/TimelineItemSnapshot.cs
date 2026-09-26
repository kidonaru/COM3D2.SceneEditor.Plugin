using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤー項目の現在値。値ペーストの変更前後を控える。
    /// 捕捉はキーフレーム登録と同じく UpdateFrame で、復元は ApplyTransformDirect で行う。
    /// タイムライン読み込み中はシーン操作が履歴に積まれないため、実運用では確定判定 (Approximately) と
    /// 自動キーフレーム登録の通知にだけ使われる (Undo は自動登録されたキーのタイムライン履歴に従う)
    /// </summary>
    public class TimelineItemSnapshot : IStateSnapshot
    {
        private readonly MTEP.ITimelineLayer _layer;
        private readonly List<string> _names = new List<string>();
        private readonly List<MTEP.ITransformData> _transforms = new List<MTEP.ITransformData>();

        private TimelineItemSnapshot(MTEP.ITimelineLayer layer)
        {
            _layer = layer;
        }

        public static TimelineItemSnapshot Capture(MTEP.ITimelineLayer layer, IList<string> names)
        {
            var snapshot = new TimelineItemSnapshot(layer);
            TimelineItemValueTransfer.Capture(layer, names, snapshot._names, snapshot._transforms);
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent()
        {
            return Capture(_layer, _names);
        }

        public void Apply(Maid maid)
        {
            foreach (var transform in _transforms)
            {
                _layer.ApplyTransformDirect(transform);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var snapshot = other as TimelineItemSnapshot;
            if (snapshot == null || snapshot._layer != _layer
                || snapshot._transforms.Count != _transforms.Count)
            {
                return false;
            }

            for (var i = 0; i < _transforms.Count; i++)
            {
                if (!MTEP.TransformDataDiff.IsApproximatelyEqual(_transforms[i], snapshot._transforms[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>レイヤーがタイムラインから外れたら書き戻さない</summary>
        public bool CanApply(Maid maid)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            return timelineManager.timeline != null && timelineManager.layers.Contains(_layer);
        }
    }
}
