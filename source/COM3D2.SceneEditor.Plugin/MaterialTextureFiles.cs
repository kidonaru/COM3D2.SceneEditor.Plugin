using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// テクスチャ差し替えの参照フォルダ (Config\SceneEditor\Toon, Texture) の一覧と読み込み。
    /// 読み込んだ Texture2D の所有と破棄は呼び出し側 (ModelMaterialTextures) が持つ
    /// </summary>
    public static class MaterialTextureFiles
    {
        private static string rootPath => PluginUtils.PluginDataPath;

        // フォルダ名 → 選択肢 (先頭は元に戻す用の空文字)。表示のたびに走査しないよう控え、「更新」で作り直す
        private static readonly Dictionary<string, List<string>> _choices = new Dictionary<string, List<string>>();

        // 同じファイルの警告を Undo/Redo の再適用のたびに出さない
        private static readonly HashSet<string> _warnedFiles = new HashSet<string>();

        public static List<string> GetChoices(string folder)
        {
            List<string> choices;
            if (!_choices.TryGetValue(folder, out choices))
            {
                choices = Scan(folder);
                _choices[folder] = choices;
            }
            return choices;
        }

        public static void Refresh()
        {
            _choices.Clear();
            _warnedFiles.Clear();
        }

        private static List<string> Scan(string folder)
        {
            var choices = new List<string> { "" };
            var directory = Path.Combine(rootPath, folder);
            try
            {
                // 置き場所が分かるよう、無ければ作っておく
                Directory.CreateDirectory(directory);
                var relativePaths = new List<string>();
                foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = MaterialTextureCatalog.ToRelativePath(rootPath, path);
                    if (relative != null)
                    {
                        relativePaths.Add(relative);
                    }
                }
                choices.AddRange(MaterialTextureCatalog.FilterFiles(folder, relativePaths));
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            return choices;
        }

        /// <summary>
        /// 相対パスのファイルを Texture2D にする。toon は端の回り込みで境目の反対側に色が滲まないよう Clamp に固定し、
        /// それ以外は元テクスチャの wrap を引き継ぐ。読めなければ null
        /// </summary>
        public static Texture2D Load(string property, string file, Texture original)
        {
            string normalized;
            if (!MaterialTextureCatalog.TryNormalize(property, file, out normalized))
            {
                WarnOnce(file, "テクスチャの指定が不正です: {0} ({1})", file, property);
                return null;
            }

            var path = Path.Combine(rootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                WarnOnce(normalized, "テクスチャが見つかりません: {0}", path);
                return null;
            }

            try
            {
                var texture = MaterialTextureCatalog.IsTexFile(normalized)
                    ? LoadTex(File.ReadAllBytes(path), normalized)
                    : LoadPng(File.ReadAllBytes(path), normalized);
                if (texture == null)
                {
                    return null;
                }

                texture.name = normalized;
                texture.wrapMode = MaterialTextureCatalog.IsToonProperty(property)
                    ? TextureWrapMode.Clamp
                    : original != null ? original.wrapMode : TextureWrapMode.Repeat;
                // 元テクスチャが無いスロットは Texture2D の既定 (Bilinear) のまま
                if (original != null)
                {
                    texture.filterMode = original.filterMode;
                }
                return texture;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return null;
            }
        }

        private static Texture2D LoadPng(byte[] bytes, string file)
        {
            // ゲームの toon と同じくミップマップなし
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                WarnOnce(file, "画像を読み込めません: {0}", file);
                return null;
            }
            return texture;
        }

        private static Texture2D LoadTex(byte[] bytes, string file)
        {
            TexFileData tex;
            if (!TexFile.TryParse(bytes, out tex))
            {
                WarnOnce(file, "tex ファイルを読み込めません: {0}", file);
                return null;
            }
            return new TextureResource(tex.width, tex.height, (TextureFormat)tex.format, null, tex.data)
                .CreateTexture2D();
        }

        private static void WarnOnce(string key, string format, params object[] args)
        {
            if (_warnedFiles.Add(key ?? ""))
            {
                MTEUtils.LogWarning(format, args);
            }
        }
    }
}
