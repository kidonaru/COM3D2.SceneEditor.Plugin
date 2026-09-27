using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>タイムラインへ書くシェーダー・テクスチャ変更一覧の組み立て</summary>
    public static class MaterialShaderSync
    {
        /// <summary>
        /// 現在の変更済みマテリアルと、まだ適用できていない保留を併せる。
        /// 同じ対象では現在の状態が勝つ。並びは保存の度に揺れないよう
        /// メイド番号 → 所有者 → マテリアル → 位置で固定する
        /// </summary>
        public static List<TimelineMaterialShaderData> Merge(
            List<TimelineMaterialShaderData> live, List<TimelineMaterialShaderData> pending)
        {
            var result = new List<TimelineMaterialShaderData>(live.Count + pending.Count);
            result.AddRange(live);
            foreach (var entry in pending)
            {
                if (!result.Exists(e => e.IsSameTarget(entry)))
                {
                    result.Add(entry);
                }
            }

            result.Sort((a, b) =>
            {
                var compare = a.maidSlotNo.CompareTo(b.maidSlotNo);
                if (compare == 0)
                {
                    compare = string.CompareOrdinal(a.owner, b.owner);
                }
                if (compare == 0)
                {
                    compare = string.CompareOrdinal(a.material, b.material);
                }
                if (compare == 0)
                {
                    compare = a.index.CompareTo(b.index);
                }
                return compare;
            });
            return result;
        }

        public static bool ListEquals(List<TimelineMaterialShaderData> a, List<TimelineMaterialShaderData> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].ContentEquals(b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Material が作り直されて現在の状態から消えたエントリを保留へ戻す。
        /// 同じ対象が現在の状態か保留に既にあれば戻さない (そちらが新しい)。戻したら true
        /// </summary>
        public static bool Requeue(
            List<TimelineMaterialShaderData> pending,
            List<TimelineMaterialShaderData> live,
            TimelineMaterialShaderData lost)
        {
            if (live.Exists(e => e.IsSameTarget(lost)) || pending.Exists(e => e.IsSameTarget(lost)))
            {
                return false;
            }
            pending.Add(lost.Clone());
            return true;
        }
    }
}
