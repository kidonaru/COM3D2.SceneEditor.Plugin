using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // VideoDisplayType enum は Timeline/VideoDisplayType.cs に分離定義している

    public class MovieManager : ManagerBase
    {
        private MoviePlayerImpl _moviePlayerImpl = null;

        private VideoDisplayType _videoDisplayType = VideoDisplayType.GUI;
        private string _loadedVideoPath = "";

        private static MovieManager _instance;
        public static MovieManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MovieManager();
                }
                return _instance;
            }
        }

        /// <summary>タイムライン未読込時に使う設定。読込中は timeline 側が正</summary>
        private readonly VideoSettings _standaloneSettings = new VideoSettings();

        /// <summary>
        /// 動画設定。タイムライン読込中は timeline 側 (TimelineXml に保存される)、
        /// 未読込時はマネージャ保持の standalone 値 (TimelineTextManager.textCount と同じ方式)
        /// </summary>
        public VideoSettings settings => timeline != null ? timeline.video : _standaloneSettings;

        private string videoPath => settings.path;

        public bool isValidPath
        {
            get
            {
                if (videoPath.Length == 0)
                {
                    return false;
                }

                return System.IO.File.Exists(videoPath);
            }
        }

        public bool isEnabled
        {
            get => isValidPath && settings.enabled;
        }

        public float currentTime
        {
            get => _moviePlayerImpl != null ? _moviePlayerImpl.currentTime : 0f;
        }

        public float duration
        {
            get => _moviePlayerImpl != null ? _moviePlayerImpl.duration : 0f;
        }

        public float frameRate
        {
            get => _moviePlayerImpl != null ? _moviePlayerImpl.frameRate : 0f;
        }

        /// <summary>プレビュー表示用の動画テクスチャ。未読込時は null</summary>
        public Texture texture
        {
            get => _moviePlayerImpl != null ? _moviePlayerImpl.texture : null;
        }

        public bool requiresVerticalFlip
        {
            get => _moviePlayerImpl != null && _moviePlayerImpl.requiresVerticalFlip;
        }

        private MovieManager()
        {
        }

        public override void Init()
        {
            TimelineManager.onStop += UpdateSeekTime;
            TimelineManager.onAnmSpeedChanged += UpdateSpeed;
            TimelineManager.onSeekCurrentFrame += UpdateSeekTime;
            TimelineManager.onClearTimeline += OnClearTimeline;
        }

        /// <summary>
        /// タイムライン破棄時に timeline 側の値を引き継ぐ。
        /// 引き継がないと MoviePlayerImpl は残ったまま表示だけ既定値へ戻り、
        /// 次のスライダー操作で配置が唐突にリセットされる
        /// </summary>
        private void OnClearTimeline()
        {
            _standaloneSettings.CopyFrom(timeline.video);
        }

        private void SetupImpl()
        {
            if (_videoDisplayType != settings.displayType)
            {
                UnloadMovie();
                _videoDisplayType = settings.displayType;
            }

            if (!isEnabled)
            {
                return;
            }

            if (_moviePlayerImpl == null)
            {
                var guid = System.Guid.NewGuid().ToString();
                var gameObject = new GameObject("MoviePlayer_" + guid);
                _moviePlayerImpl = gameObject.AddComponent<MoviePlayerImpl>();
            }
        }

        public void LoadMovie()
        {
            if (!isEnabled)
            {
                return;
            }

            if (_loadedVideoPath == videoPath)
            {
                return;
            }
            _loadedVideoPath = videoPath;

            SetupImpl();

            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.LoadMovie(videoPath);
            }
        }

        public void UnloadMovie()
        {
            if (_moviePlayerImpl != null)
            {
                Object.Destroy(_moviePlayerImpl.gameObject);
                _moviePlayerImpl = null;
            }
            _loadedVideoPath = "";
        }

        public void ReloadMovie()
        {
            UnloadMovie();
            LoadMovie();
        }

        public void UpdateTransform()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateTransform();
            }
        }

        public void UpdateVolume()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateVolume();
            }
        }

        public void UpdateSpeed()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateSpeed();
            }
        }

        public void UpdateSeekTime()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateSeekTime();
            }
        }

        public void UpdateColor()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateColor();
            }
        }

        public void UpdateMesh()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateMesh();
            }
        }

        public void UpdateShader()
        {
            if (_moviePlayerImpl != null)
            {
                _moviePlayerImpl.UpdateShader();
            }
        }

        public override void OnLoad()
        {
            ReloadMovie();
        }

        public override void OnPluginDisable()
        {
            UnloadMovie();
        }
    }
}