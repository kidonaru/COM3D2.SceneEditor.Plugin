using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TimelineBundleManager : ManagerBase
    {
        // MTE 由来のシェーダ・テクスチャと SE 独自のステージライトをまとめて UnityProject (Unity 5.6) でビルドした
        // バンドル。5.6 製なので COM3D2 (2.0) / COM3D2.5 の両方で読める
        private static readonly string AssetBundleName = "se_bundle";
        private static readonly string ShaderBasePath = "Assets/Shaders/";
        private static readonly string ResoucesBasePath = "Assets/Resources/";

        public static TimelineBundleManager _instance = null;
        public static TimelineBundleManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineBundleManager();
                }
                return _instance;
            }
        }

        private Texture2D _lockIcon = null;
        public Texture2D lockIcon
        {
            get
            {
                if (_lockIcon == null) _lockIcon = LoadTexture("lock_icon");
                return _lockIcon;
            }
        }

        private Texture2D _unlockIcon = null;
        public Texture2D unlockIcon
        {
            get
            {
                if (_unlockIcon == null) _unlockIcon = LoadTexture("unlock_icon");
                return _unlockIcon;
            }
        }

        private Texture2D _changeIcon = null;
        public Texture2D changeIcon
        {
            get
            {
                if (_changeIcon == null) _changeIcon = LoadTexture("change_icon");
                return _changeIcon;
            }
        }

        private Texture2D _updateIcon = null;
        public Texture2D updateIcon
        {
            get
            {
                if (_updateIcon == null) _updateIcon = LoadTexture("update_icon");
                return _updateIcon;
            }
        }

        private AssetBundle _assetBundle = null;

        public TimelineBundleManager()
        {
            _assetBundle = LoadAssetBundle(AssetBundleName);
        }

        public bool IsValid()
        {
            return _assetBundle != null;
        }

        private Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();

        public Material LoadMaterial(string materialName)
        {
            if (!IsValid())
            {
                return null;
            }

            var path = ShaderBasePath + materialName + ".mat";
            Material material;
            if (_materialCache.TryGetValue(path, out material))
            {
                return new Material(material);
            }

            material = _assetBundle.LoadAsset<Material>(path);
            if (material == null)
            {
                MTEUtils.LogError("マテリアルが見つかりません: {0}", path);
                return null;
            }

            _materialCache.Add(path, material);

            return new Material(material);
        }

        public Texture2D LoadTexture(string textureName)
        {
            if (!IsValid())
            {
                return null;
            }

            var path = ResoucesBasePath + textureName + ".png";
            var texture = _assetBundle.LoadAsset<Texture2D>(path);
            if (texture == null)
            {
                MTEUtils.LogError("テクスチャが見つかりません: {0}", path);
                return null;
            }

            return texture;
        }

        public Byte[] LoadBytes(string bytesName)
        {
            if (!IsValid())
            {
                return null;
            }

            var path = ResoucesBasePath + bytesName + ".bytes";
            var asset = _assetBundle.LoadAsset<TextAsset>(path);
            if (asset == null)
            {
                MTEUtils.LogError("バイナリが見つかりません: {0}", path);
                return null;
            }

            return asset.bytes;
        }

        private AssetBundle LoadAssetBundle(string assetBundleName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(assetBundleName))
            {
                if (stream == null)
                {
                    MTEUtils.LogError("アセットバンドルが見つかりません: {0}", assetBundleName);
                    return null;
                }

                byte[] binary = new byte[stream.Length];
                stream.Read(binary, 0, binary.Length);

                var assetBundle = AssetBundle.LoadFromMemory(binary);
                if (assetBundle == null)
                {
                    MTEUtils.LogError("アセットバンドルのロードに失敗しました: {0}", assetBundleName);
                }
                return assetBundle;
            }
        }
    }
}