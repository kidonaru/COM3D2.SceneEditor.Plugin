using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 対象 Transform と記録時の TRS の対応表。
    /// 着替え等で破棄された参照はスキップして残りだけ復元する
    /// </summary>
    public class BoneTrsMap
    {
        private readonly Dictionary<Transform, BoneTrs> _bones
            = new Dictionary<Transform, BoneTrs>();

        public IEnumerable<Transform> targets => _bones.Keys;

        public int count => _bones.Count;

        /// <summary>未記録の対象だけその時点の値で捕捉する</summary>
        public void AddBones(IEnumerable<Transform> targetBones)
        {
            if (targetBones == null)
            {
                return;
            }

            foreach (var bone in targetBones)
            {
                if (bone != null && !_bones.ContainsKey(bone))
                {
                    _bones[bone] = BoneTrs.Capture(bone);
                }
            }
        }

        /// <summary>記録済みの対象を書き戻す。破棄された対象は飛ばす</summary>
        public void Apply()
        {
            foreach (var pair in _bones)
            {
                if (pair.Key != null)
                {
                    pair.Value.ApplyTo(pair.Key);
                }
            }
        }

        /// <summary>復元先が 1 つでも残っているか。全滅したエントリの適用スキップ判定に使う</summary>
        public bool hasAliveBones
        {
            get
            {
                foreach (var bone in _bones.Keys)
                {
                    if (bone != null)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool Approximately(BoneTrsMap other)
        {
            // after は before と同じ対象集合で捕捉するため、件数の不一致は
            // 確定待ち中に対象が破棄されたときのみ起こる。その場合は変化ありとみなす
            if (_bones.Count != other._bones.Count)
            {
                return false;
            }

            foreach (var pair in _bones)
            {
                BoneTrs otherTrs;
                if (!other._bones.TryGetValue(pair.Key, out otherTrs)
                    || !pair.Value.Approximately(otherTrs))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
