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
        /// モデル複製用。元モデル (sourceOwner) のエントリを複製し、所有者だけ newOwner に変える。
        /// エントリは Clone で丸ごと写すので、後からエントリへ足した項目 (テクスチャ差し替え等) も一緒に写る。
        /// メイドのエントリ (maidSlotNo >= 0) は所有者がスロット名なので対象外
        /// </summary>
        public static List<TimelineMaterialShaderData> CopyForModel(
            List<TimelineMaterialShaderData> entries, string sourceOwner, string newOwner)
        {
            var result = new List<TimelineMaterialShaderData>();
            foreach (var entry in entries)
            {
                if (entry.maidSlotNo >= 0 || entry.owner != sourceOwner)
                {
                    continue;
                }

                var copy = entry.Clone();
                copy.owner = newOwner;
                result.Add(copy);
            }
            return result;
        }

        /// <summary>モデル (owner) のエントリを entries から消す。メイドのエントリは消さない。消した件数を返す</summary>
        public static int RemoveForModel(List<TimelineMaterialShaderData> entries, string owner)
        {
            return entries.RemoveAll(e => e.maidSlotNo < 0 && e.owner == owner);
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
