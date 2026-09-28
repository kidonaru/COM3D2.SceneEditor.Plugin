using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// CM3D2 (ゲームの互換モード) の背景 1 件。
    /// PhotoBGData はコンストラクタが private で id も 2.5 と衝突するため、別の型で持つ
    /// </summary>
    public class Cm3d2BgEntry
    {
        /// <summary>「CM3D2:」を付けたカテゴリ名</summary>
        public readonly string category;
        public readonly string name;
        /// <summary>BgMgr.ChangeBg に渡す prefab 名。保存もこの名前で行う</summary>
        public readonly string prefabName;

        public Cm3d2BgEntry(string category, string name, string prefabName)
        {
            this.category = category;
            this.name = name;
            this.prefabName = prefabName;
        }
    }

    /// <summary>
    /// CM3D2 の phot_bg_list.nei の行から背景一覧を組み立てる。
    /// ファイルの読み込みは BackgroundUtils が行い、ここはゲームの型に触れない (テストから呼ぶため)
    /// </summary>
    public static class Cm3d2BgList
    {
        public const string ListFileName = "phot_bg_list.nei";
        public const string EnabledListName = "phot_bg_enabled_list";
        /// <summary>2.5 の背景とカテゴリを見分けるための接頭辞</summary>
        public const string CategoryPrefix = "CM3D2:";

        // phot_bg_list.nei の列 (ID / カテゴリー / BG表示名 / 作成プレファブ名 / 必要パック)
        public const int ColumnId = 0;
        public const int ColumnCategory = 1;
        public const int ColumnName = 2;
        public const int ColumnPrefabName = 3;
        public const int ColumnRequiredPack = 4;
        public const int ColumnCount = 5;

        public static string ToCategoryName(string category)
        {
            return CategoryPrefix + (category ?? "");
        }

        /// <summary>
        /// 行を背景一覧へ変換する。絞り込みはゲームの PhotoBGData.Create に合わせ、
        /// 加えて 2.5 の一覧 (existingPrefabNames) と prefab 名が重なる行を除く。
        /// enabledIds が null か空なら ID では絞り込まない
        /// </summary>
        public static List<Cm3d2BgEntry> Build(
            IEnumerable<string[]> rows,
            ICollection<int> enabledIds,
            Predicate<string> isPackEnabled,
            IEnumerable<string> existingPrefabNames)
        {
            var entries = new List<Cm3d2BgEntry>();
            if (rows == null)
            {
                return entries;
            }

            // BgMgr.CreateAssetBundle は prefab 名を小文字化して引くため、大文字小文字違いは同じ背景
            var seenPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingPrefabNames != null)
            {
                foreach (var prefabName in existingPrefabNames)
                {
                    if (!string.IsNullOrEmpty(prefabName))
                    {
                        seenPrefabNames.Add(prefabName);
                    }
                }
            }

            var filterById = enabledIds != null && enabledIds.Count > 0;

            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                int id;
                if (!int.TryParse(GetCell(row, ColumnId).Trim(), out id))
                {
                    continue;
                }
                if (filterById && !enabledIds.Contains(id))
                {
                    continue;
                }

                var prefab = GetCell(row, ColumnPrefabName);
                if (string.IsNullOrEmpty(prefab))
                {
                    continue;
                }

                var pack = GetCell(row, ColumnRequiredPack);
                if (!string.IsNullOrEmpty(pack) && isPackEnabled != null && !isPackEnabled(pack))
                {
                    continue;
                }

                if (!seenPrefabNames.Add(prefab))
                {
                    continue;
                }

                var name = GetCell(row, ColumnName);
                entries.Add(new Cm3d2BgEntry(
                    ToCategoryName(GetCell(row, ColumnCategory)),
                    string.IsNullOrEmpty(name) ? prefab : name,
                    prefab));
            }
            return entries;
        }

        /// <summary>prefab 名 (BgMgr.GetBGName() の値) から引く。大文字小文字は区別しない</summary>
        public static Cm3d2BgEntry Find(IList<Cm3d2BgEntry> entries, string prefabName)
        {
            if (entries == null || string.IsNullOrEmpty(prefabName))
            {
                return null;
            }
            foreach (var entry in entries)
            {
                if (string.Equals(entry.prefabName, prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>カテゴリ名を出現順に重複なく返す</summary>
        public static List<string> GetCategories(IList<Cm3d2BgEntry> entries)
        {
            var categories = new List<string>();
            if (entries == null)
            {
                return categories;
            }
            foreach (var entry in entries)
            {
                if (!categories.Contains(entry.category))
                {
                    categories.Add(entry.category);
                }
            }
            return categories;
        }

        private static string GetCell(string[] row, int column)
        {
            return column < row.Length ? (row[column] ?? "") : "";
        }
    }
}
