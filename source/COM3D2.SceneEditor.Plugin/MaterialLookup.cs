using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>保存されたマテリアル名 + 位置から、現在の一覧の位置を引く</summary>
    public static class MaterialLookup
    {
        /// <summary>
        /// 同名マテリアルが複数ある場合に備え、保存時の位置の名前一致を優先し、無ければ先頭の名前一致。
        /// 見つからなければ -1
        /// </summary>
        public static int FindIndex(IList<string> names, string name, int index)
        {
            if (index >= 0 && index < names.Count && names[index] == name)
            {
                return index;
            }
            return names.IndexOf(name);
        }
    }
}
