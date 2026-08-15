using System;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    public class ConfigManager : ManagerBase
    {
        private Config _config = new Config();
        public new Config config => _config;

        private static ConfigManager _instance = null;
        public static ConfigManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }

        private bool _isLoaded = false;

        /// <summary>初回起動 (config ファイルが存在しなかった) かどうか</summary>
        private bool _isFirstLaunch = false;

        private ConfigManager()
        {
        }

        /// <summary>
        /// 初回起動フラグを消費する。初回起動なら true を返し、以降は false を返す。
        /// 初回限定の初期化 (既定レイアウトの適用など) を一度だけ走らせるために使う。
        /// 呼び出し側で黙ってフラグを潰さないよう、起動フック 1 箇所からのみ呼ぶこと
        /// </summary>
        public bool ConsumeFirstLaunch()
        {
            if (!_isFirstLaunch)
            {
                return false;
            }
            _isFirstLaunch = false;
            return true;
        }

        public override void Init()
        {
            if (!_isLoaded)
            {
                LoadConfigXml();
                SaveConfigXml();
            }
        }

        public override void Update()
        {
            if (config.dirty && Input.GetMouseButtonUp(0))
            {
                SaveConfigXml();
            }
        }

        public void LoadConfigXml()
        {
            try
            {
                var path = PluginUtils.ConfigPath;
                if (!File.Exists(path))
                {
                    // 初回起動。既定値のまま読み込み済みとして扱い、Init が二重に走らないようにする
                    _isFirstLaunch = true;
                    _isLoaded = true;
                    return;
                }

                var serializer = new XmlSerializer(typeof(Config));
                using (var stream = new FileStream(path, FileMode.Open))
                {
                    _config = (Config)serializer.Deserialize(stream);
                    _config.ConvertVersion();
                }

                _isLoaded = true;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void SaveConfigXml()
        {
            MTEUtils.LogDebug("設定保存中...");
            try
            {
                var path = PluginUtils.ConfigPath;
                // Config フォルダが無い環境でも保存できるようにする
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var serializer = new XmlSerializer(typeof(Config));
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    serializer.Serialize(stream, config);
                }

                // 書き込みが成功してから倒す。失敗時は dirty のままにして次の機会に再試行させる
                config.dirty = false;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void ResetConfig()
        {
            _config = new Config();
            SaveConfigXml();
        }
    }
}
