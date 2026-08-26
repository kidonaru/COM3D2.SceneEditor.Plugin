using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデル修飾名 ("{model.name}/{生名}") の組み立て。
    /// 編集側は生名 (Transform 名・シェイプキー名) そのまま、タイムライン側はモデル名で修飾した名前を
    /// 使うため、その橋渡しをここ 1 箇所に閉じ込める。ボーンとシェイプキーで共有する。
    /// 修飾規則は ModelBone.name (Timeline/ModelBoneController.cs) および
    /// ModelBlendShape.name (Timeline/BlendShapeController.cs) と必ず一致させること
    /// </summary>
    public static class ModelQualifiedNames
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

        /// <summary>ボーン編集済みエントリを修飾名にして result へ積む (result はクリアしない)</summary>
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
