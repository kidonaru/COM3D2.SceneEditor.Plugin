using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド拡張ボーン名 ("{slotName}/{boneName}") の組み立て。
    /// 編集側はスロット名とボーン名を別々に持ち、タイムライン側は連結名で扱うため、
    /// その橋渡しをここ 1 箇所に閉じ込める。
    /// 修飾規則は MTE の ExtendBoneCache.AddEntity (Timeline/ExtendBoneCache.cs) と
    /// 必ず一致させること。スロット名は双方とも TBody.SlotID 名 (= TBodySkin.Category)
    /// </summary>
    public static class SlotQualifiedNames
    {
        /// <summary>タイムライン側の拡張ボーン名を作る。名前が欠けていれば null</summary>
        public static string Qualify(string slotName, string boneName)
        {
            if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            return slotName + "/" + boneName;
        }

        /// <summary>ボーン編集済みエントリを拡張ボーン名にして result へ積む (result はクリアしない)</summary>
        public static void Collect(List<BoneEditEntry> entries, List<string> result)
        {
            if (entries == null || result == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var name = Qualify(entry.slotName, entry.boneName);
                if (name != null)
                {
                    result.Add(name);
                }
            }
        }
    }
}
