using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルボーンの名前変換。
    /// 編集側 (BoneEditStore) は Transform 名そのまま、タイムライン側 (StudioModelManager.boneNames) は
    /// モデル名で修飾した名前を使うため、その橋渡しをここ 1 箇所に閉じ込める。
    /// 修飾規則は ModelBone.name (Timeline/ModelBoneController.cs) と必ず一致させること
    /// </summary>
    public static class ModelBoneTrackedNames
    {
        /// <summary>タイムライン側のモデル修飾名を作る。名前が欠けていれば null</summary>
        public static string Qualify(string modelName, string boneName)
        {
            if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            return modelName + "/" + boneName;
        }

        /// <summary>編集済みエントリを修飾名にして result へ積む (result はクリアしない)</summary>
        public static void Collect(string modelName, List<BoneEditEntry> entries, List<string> result)
        {
            if (entries == null || result == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var name = Qualify(modelName, entry.boneName);
                if (name != null)
                {
                    result.Add(name);
                }
            }
        }
    }
}
