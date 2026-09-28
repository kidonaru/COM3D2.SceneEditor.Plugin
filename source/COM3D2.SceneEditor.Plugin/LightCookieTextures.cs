using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スポットライトの輪郭 (cookie) テクスチャの生成・読込・キャッシュ。
    /// 同じ硬さ・同じ画像の灯はテクスチャを共有する
    /// </summary>
    public static class LightCookieTextures
    {
        private const int GeneratedSize = 256;
        // 硬さはこの刻みで丸めてキャッシュする (スライダーのドラッグで生成が増え続けないように)
        private const int HardnessSteps = 100;

        private static readonly Dictionary<int, Texture2D> _generated = new Dictionary<int, Texture2D>();
        // 読めなかった画像も null で覚え、警告を毎回出さない
        private static readonly Dictionary<string, Texture2D> _images =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _imageNames = null;

        public static readonly string directory = Path.Combine(PluginUtils.PluginDataPath, "LightCookie");

        public static Texture2D GetGenerated(float hardness)
        {
            var key = (int)Math.Round(Math.Max(0f, Math.Min(1f, hardness)) * HardnessSteps);
            Texture2D texture;
            if (_generated.TryGetValue(key, out texture) && texture != null)
            {
                return texture;
            }

            var alpha = LightCookieAlpha.BuildRadial(GeneratedSize, (float)key / HardnessSteps);
            texture = CreateAlphaTexture(alpha, GeneratedSize, GeneratedSize);
            _generated[key] = texture;
            return texture;
        }

        public static Texture2D GetImage(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            Texture2D texture;
            if (_images.TryGetValue(relativePath, out texture))
            {
                return texture;
            }

            texture = LoadImage(relativePath);
            _images[relativePath] = texture;
            return texture;
        }

        private static Texture2D LoadImage(string relativePath)
        {
            // relativePath はプリセット XML 由来の外部入力なのでフォルダ外への脱出を弾く
            var path = ImagePathResolver.Resolve(directory, relativePath);
            if (path == null)
            {
                MTEUtils.LogWarning("ライトの輪郭画像のパスが不正です: {0}", relativePath);
                return null;
            }
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("ライトの輪郭画像が見つかりません: {0}", path);
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ライトの輪郭画像を読み込めません: {0} ({1})", path, e.Message);
                return null;
            }

            // サイズは LoadImage が実画像で上書きするためダミー
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(bytes))
                {
                    MTEUtils.LogWarning("ライトの輪郭画像を読み込めません: {0}", path);
                    return null;
                }
                var alpha = LightCookieAlpha.FromPixels(source.GetPixels32(), source.width, source.height);
                return CreateAlphaTexture(alpha, source.width, source.height);
            }
            finally
            {
                Object.Destroy(source);
            }
        }

        private static Texture2D CreateAlphaTexture(byte[] alpha, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.Alpha8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.LoadRawTextureData(alpha);
            texture.Apply(false);
            return texture;
        }

        /// <summary>画像フォルダ配下の PNG の相対パス一覧。フォルダが無ければ空</summary>
        public static List<string> GetImageNames()
        {
            if (_imageNames == null)
            {
                _imageNames = ListImageNames();
            }
            return _imageNames;
        }

        private static List<string> ListImageNames()
        {
            var result = new List<string>();
            var dir = directory;

            // 一覧は描画のたびに参照するので、失敗しても空で覚えて毎フレーム例外を出さない
            try
            {
                // 置き場所が分かるよう、無ければ作っておく
                Directory.CreateDirectory(dir);
                var rootPath = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var path in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                {
                    result.Add(Path.GetFullPath(path).Substring(rootPath.Length));
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ライトの輪郭画像の一覧を取得できません: {0} ({1})", dir, e.Message);
                result.Clear();
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>画像の一覧と読込済み画像を捨てる。呼び出し側は灯へ再適用すること</summary>
        public static void Reload()
        {
            DestroyAll(_images.Values);
            _images.Clear();
            _imageNames = null;
        }

        public static void ReleaseAll()
        {
            Reload();
            DestroyAll(_generated.Values);
            _generated.Clear();
        }

        private static void DestroyAll(IEnumerable<Texture2D> textures)
        {
            foreach (var texture in textures)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
        }
    }
}
