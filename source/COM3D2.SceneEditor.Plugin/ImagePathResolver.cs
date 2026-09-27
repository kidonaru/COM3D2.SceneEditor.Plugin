using System;
using System.IO;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 画像フォルダ配下の実ファイルパスの解決 (PNG 配置とライトの輪郭画像で共用)。
    /// ゲーム外のテストから呼べるよう Unity に依存しない
    /// </summary>
    public static class ImagePathResolver
    {
        /// <summary>
        /// relativePath はプリセットやタイムラインの XML 由来の外部入力なので、絶対パス指定や
        /// ".." による出所外への脱出、パスに使えない文字を弾く。範囲外・不正なら null
        /// </summary>
        public static string Resolve(string dir, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(relativePath))
                {
                    return null;
                }

                var rootPath = Path.GetFullPath(dir);
                if (!rootPath.EndsWith("\\") && !rootPath.EndsWith("/"))
                {
                    rootPath += Path.DirectorySeparatorChar;
                }

                var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
                if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return fullPath;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }
    }
}
