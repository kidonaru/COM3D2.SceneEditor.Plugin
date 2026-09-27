using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアルのテクスチャ差し替えで扱うプロパティと、差し替えに使えるファイルの決まり。
    /// ファイルは SceneEditor フォルダからの相対パス (/ 区切り) で持ち、
    /// プロパティごとの参照フォルダの外は指せない (他の環境から渡された XML でフォルダ外を読まないため)
    /// </summary>
    public static class MaterialTextureCatalog
    {
        public const string ToonFolder = "Toon";
        public const string TextureFolder = "Texture";

        /// <summary>行の並び順</summary>
        public static readonly string[] Properties =
        {
            "_MainTex", "_ShadowTex", "_ToonRamp", "_ShadowRateToon", "_OutlineToonRamp",
        };

        private static readonly string[] Labels =
        {
            "テクスチャ", "影テクスチャ", "トゥーン", "影の濃さ", "輪郭トゥーン",
        };

        private static readonly string[] Extensions = { ".png", ".tex" };

        public static bool IsToonProperty(string property)
        {
            return property == "_ToonRamp" || property == "_ShadowRateToon" || property == "_OutlineToonRamp";
        }

        /// <summary>プロパティの参照フォルダ名。対象外は null</summary>
        public static string GetFolder(string property)
        {
            if (IsToonProperty(property))
            {
                return ToonFolder;
            }
            if (property == "_MainTex" || property == "_ShadowTex")
            {
                return TextureFolder;
            }
            return null;
        }

        public static string GetLabel(string property)
        {
            var index = Array.IndexOf(Properties, property);
            return index >= 0 ? Labels[index] : property;
        }

        /// <summary>
        /// 相対パスを / 区切りに揃える。絶対パス・親への移動・ドライブ指定・対象外の拡張子は null
        /// </summary>
        public static string NormalizeFile(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return null;
            }
            var trimmed = file.Trim().Replace('\\', '/');
            if (trimmed.Length == 0 || trimmed[0] == '/' || trimmed.IndexOf(':') >= 0)
            {
                return null;
            }

            var parts = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == "." || part == "..")
                {
                    return null;
                }
            }
            var normalized = string.Join("/", parts);
            return HasImageExtension(normalized) ? normalized : null;
        }

        /// <summary>正規化したうえで、プロパティの参照フォルダ内 (サブフォルダ可) を指していれば true</summary>
        public static bool TryNormalize(string property, string file, out string normalized)
        {
            normalized = null;
            var folder = GetFolder(property);
            var candidate = NormalizeFile(file);
            if (folder == null || candidate == null
                || !candidate.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            normalized = candidate;
            return true;
        }

        public static bool IsTexFile(string file)
        {
            return file.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasImageExtension(string file)
        {
            foreach (var extension in Extensions)
            {
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    && file.Length > extension.Length
                    && file[file.Length - extension.Length - 1] != '/')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>root 配下の絶対パスを root からの相対パス (/ 区切り) にする。root の外は null</summary>
        public static string ToRelativePath(string root, string fullPath)
        {
            var prefix = root.TrimEnd('\\', '/') + "\\";
            var path = fullPath.Replace('/', '\\');
            if (!path.StartsWith(prefix.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return path.Substring(prefix.Length).Replace('\\', '/');
        }

        /// <summary>相対パスの一覧から、folder 内の画像だけを正規化して自然順に並べる</summary>
        public static List<string> FilterFiles(string folder, IEnumerable<string> relativePaths)
        {
            var result = new List<string>();
            foreach (var path in relativePaths)
            {
                var normalized = NormalizeFile(path);
                if (normalized != null
                    && normalized.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                    && !result.Exists(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(normalized);
                }
            }
            // 自然順の比較は MTEUtils/NaturalStringComparer.cs の既存クラス
            result.Sort(new NaturalStringComparer());
            return result;
        }

        /// <summary>一覧の表示名。参照フォルダ名は全行で同じなので除く</summary>
        public static string GetDisplayName(string file)
        {
            var slash = file.IndexOf('/');
            return slash >= 0 ? file.Substring(slash + 1) : file;
        }
    }
}
