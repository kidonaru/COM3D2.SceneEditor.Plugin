using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤーが生まれた時点のシーン断面を (レイヤー型, スロット番号) で保管する。
    ///
    /// レイヤーのインスタンスには持たせない。undo/redo の TimelineManager.UpdateTimeline が
    /// ClearTimeline → FromXml → LayerInit を通すため、レイヤーは差し替えのたびに別物になる。
    /// 断面の生存期間はタイムラインのセッション単位で、ResetTimelineState でまとめて捨てる。
    ///
    /// 渡された FrameXml / BoneXml はコピーせずそのまま抱える。呼び出し側は
    /// 使い回しのオブジェクトではなく FrameData.ToXml() の戻り値を渡すこと
    /// </summary>
    public class TimelineLayerBaselineStore
    {
        private readonly Dictionary<Type, Dictionary<int, MTEP.FrameXml>> _map
            = new Dictionary<Type, Dictionary<int, MTEP.FrameXml>>();

        /// <summary>
        /// 断面を控えた時点でレイヤーが見ていた対象の数。
        /// 毎フレームの「対象が増えていないか」の判定を辞書引き 1 回で済ませるために持つ
        /// </summary>
        private readonly Dictionary<Type, Dictionary<int, int>> _coverageMap
            = new Dictionary<Type, Dictionary<int, int>>();

        /// <summary>保管している断面の総数 (テストと調査用)</summary>
        public int count
        {
            get
            {
                var total = 0;
                foreach (var slotMap in _map.Values)
                {
                    total += slotMap.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// 誕生時の断面を記録する。同じキーは置き換える
        /// (削除したレイヤーを追加し直したときに基準を取り直すため)
        /// </summary>
        public void Set(Type layerType, int slotNo, MTEP.FrameXml frameXml)
        {
            if (layerType == null || frameXml == null)
            {
                return;
            }
            GetOrCreateSlotMap(layerType)[slotNo] = frameXml;
        }

        /// <summary>
        /// 追跡系レイヤーが対象を増やしたときの積み増し。
        /// boneNames に挙がっていて、かつまだ記録の無いボーンだけを足す (先勝ち)。
        /// 記録済みのボーンを上書きしないのは、誕生時の値こそが戻すべき基準だから
        /// </summary>
        public void Merge(
            Type layerType, int slotNo, MTEP.FrameXml frameXml, List<string> boneNames)
        {
            if (layerType == null || frameXml == null || frameXml.bones == null
                || boneNames == null || boneNames.Count == 0)
            {
                return;
            }

            MTEP.FrameXml baseline;
            TryGet(layerType, slotNo, out baseline);

            // 空の断面を作らないよう、足すボーンを決めてから登録する
            var knownNames = new HashSet<string>();
            if (baseline != null && baseline.bones != null)
            {
                foreach (var bone in baseline.bones)
                {
                    if (bone != null && bone.transform != null)
                    {
                        knownNames.Add(bone.transform.name);
                    }
                }
            }

            var targetNames = new HashSet<string>(boneNames);
            var addedBones = new List<MTEP.BoneXml>();
            foreach (var bone in frameXml.bones)
            {
                if (bone == null || bone.transform == null)
                {
                    continue;
                }
                var name = bone.transform.name;
                if (!targetNames.Contains(name) || knownNames.Contains(name))
                {
                    continue;
                }
                knownNames.Add(name);
                addedBones.Add(bone);
            }

            if (addedBones.Count == 0)
            {
                return;
            }

            if (baseline == null)
            {
                baseline = new MTEP.FrameXml { frameNo = 0, bones = new List<MTEP.BoneXml>() };
                GetOrCreateSlotMap(layerType)[slotNo] = baseline;
            }
            else if (baseline.bones == null)
            {
                baseline.bones = new List<MTEP.BoneXml>();
            }

            baseline.bones.AddRange(addedBones);
        }

        public bool TryGet(Type layerType, int slotNo, out MTEP.FrameXml frameXml)
        {
            frameXml = null;

            Dictionary<int, MTEP.FrameXml> slotMap;
            if (layerType == null || !_map.TryGetValue(layerType, out slotMap))
            {
                return false;
            }
            return slotMap.TryGetValue(slotNo, out frameXml);
        }

        /// <summary>
        /// 断面を控えた時点の対象数を記録する。
        /// 増えたときだけ積み増したいので、比較の基準を残しておく
        /// </summary>
        public void SetCoverage(Type layerType, int slotNo, int boneNameCount)
        {
            if (layerType == null)
            {
                return;
            }

            Dictionary<int, int> slotMap;
            if (!_coverageMap.TryGetValue(layerType, out slotMap))
            {
                slotMap = new Dictionary<int, int>();
                _coverageMap[layerType] = slotMap;
            }
            slotMap[slotNo] = boneNameCount;
        }

        /// <summary>記録済みの対象数。未記録なら -1</summary>
        public int GetCoverage(Type layerType, int slotNo)
        {
            Dictionary<int, int> slotMap;
            if (layerType == null || !_coverageMap.TryGetValue(layerType, out slotMap))
            {
                return -1;
            }

            int coverage;
            return slotMap.TryGetValue(slotNo, out coverage) ? coverage : -1;
        }

        public void Clear()
        {
            _map.Clear();
            _coverageMap.Clear();
        }

        private Dictionary<int, MTEP.FrameXml> GetOrCreateSlotMap(Type layerType)
        {
            Dictionary<int, MTEP.FrameXml> slotMap;
            if (!_map.TryGetValue(layerType, out slotMap))
            {
                slotMap = new Dictionary<int, MTEP.FrameXml>();
                _map[layerType] = slotMap;
            }
            return slotMap;
        }
    }
}
