using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアルのシェーダー変更で選べるシェーダーの一覧。
    /// NPRShader のシェーダーは AssetBundle 由来で Shader.Find に載らないため、
    /// 読み込み済みの Shader 全体から名前の接頭辞で拾う (NPRShader への参照は持たない)
    /// </summary>
    public static class ShaderCatalog
    {
        /// <summary>候補にする接頭辞。並びがそのまま一覧のグループ順になる (バニラ → NPRShader)</summary>
        private static readonly string[] Prefixes = { "CM3D2/", "com3d2mod/" };

        /// <summary>接頭辞で絞り、重複を除き、接頭辞の順 → 名前順に並べる</summary>
        public static List<string> FilterNames(IEnumerable<string> names)
        {
            var groups = new List<string>[Prefixes.Length];
            for (var i = 0; i < groups.Length; i++)
            {
                groups[i] = new List<string>();
            }

            var seen = new HashSet<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue;
                }
                for (var i = 0; i < Prefixes.Length; i++)
                {
                    if (name.StartsWith(Prefixes[i], System.StringComparison.Ordinal))
                    {
                        groups[i].Add(name);
                        break;
                    }
                }
            }

            var result = new List<string>();
            foreach (var group in groups)
            {
                group.Sort(string.CompareOrdinal);
                result.AddRange(group);
            }
            return result;
        }

        /// <summary>読み込み済みで使えるシェーダーを一覧順に返す。同名は先勝ち</summary>
        public static List<Shader> GetShaders()
        {
            var byName = new Dictionary<string, Shader>();
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (shader != null && shader.isSupported && !byName.ContainsKey(shader.name))
                {
                    byName[shader.name] = shader;
                }
            }

            var result = new List<Shader>();
            foreach (var name in FilterNames(byName.Keys))
            {
                result.Add(byName[name]);
            }
            return result;
        }

        /// <summary>保存されたシェーダー名から引く。見つからなければ null</summary>
        public static Shader Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var shader = Shader.Find(name);
            if (shader != null)
            {
                return shader;
            }

            // NPRShader のシェーダーは Shader.Find では見つからない
            foreach (var loaded in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (loaded != null && loaded.name == name)
                {
                    return loaded;
                }
            }
            return null;
        }
    }
}
