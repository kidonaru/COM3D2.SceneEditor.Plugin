using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// テクスチャ差し替え 1 件 (シェーダーのプロパティ名と、SceneEditor フォルダからの相対パス)。
    /// タイムライン・プリセット・Undo・クリップボードで共有するため不変にする
    /// </summary>
    public class MaterialTextureOverride
    {
        public readonly string property;
        public readonly string file;

        public MaterialTextureOverride(string property, string file)
        {
            this.property = property;
            this.file = file;
        }

        /// <summary>保存と比較の順を揃えるため、プロパティ名の昇順に並べる</summary>
        public static void Sort(List<MaterialTextureOverride> list)
        {
            list.Sort((a, b) => string.CompareOrdinal(a.property, b.property));
        }

        /// <summary>並びも含めて同じか。呼び出し側は Sort 済みの一覧を渡す</summary>
        public static bool ListEquals(List<MaterialTextureOverride> a, List<MaterialTextureOverride> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].property != b[i].property || a[i].file != b[i].file)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
