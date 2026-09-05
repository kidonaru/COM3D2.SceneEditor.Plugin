using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // VideoDisplayType enum は Timeline/VideoDisplayType.cs に分離定義している

    public class MovieManager : ManagerBase
    {
        public const int MinVideoCount = 1;
        public const int MaxVideoCount = 4;

        /// <summary>index ごとのプレイヤー。未読込は null。settingsList と同じ長さに保つ</summary>
        private readonly List<MoviePlayerImpl> _players = new List<MoviePlayerImpl>();
        private readonly List<string> _loadedVideoPaths = new List<string>();
        private readonly List<VideoDisplayType> _loadedDisplayTypes = new List<VideoDisplayType>();

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
        private readonly List<VideoSettings> _standaloneSettingsList = new List<VideoSettings> { new VideoSettings() };

        /// <summary>
        /// 動画設定の一覧。タイムライン読込中は timeline 側 (TimelineXml に保存される)、
        /// 未読込時はマネージャ保持の standalone 値 (TimelineTextManager.textCount と同じ方式)
        /// </summary>
        public List<VideoSettings> settingsList => timeline != null ? timeline.videos : _standaloneSettingsList;

        public int videoCount
        {
            get => settingsList.Count;
            set
            {
                var count = Mathf.Clamp(value, MinVideoCount, MaxVideoCount);
                var list = settingsList;

                // 減らす分はプレイヤーを先に破棄する (設定を消してから Unload すると添字がずれる)
                while (list.Count > count)
                {
                    UnloadMovie(list.Count - 1);
                    list.RemoveAt(list.Count - 1);
                }

                // 増やした分はパス未設定の既定値。ユーザーがパスを選んだ時点で読み込む
                while (list.Count < count)
                {
                    list.Add(new VideoSettings());
                }

                SyncPlayerListLength();
            }
        }

        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < settingsList.Count;
        }

        public VideoSettings GetSettings(int index)
        {
            return settingsList[index];
        }

        public bool IsValidPath(int index)
        {
            var path = GetSettings(index).path;
            return path.Length > 0 && System.IO.File.Exists(path);
        }

        public bool IsEnabled(int index)
        {
            return IsValidPath(index) && GetSettings(index).enabled;
        }

        private MoviePlayerImpl GetPlayer(int index)
        {
            SyncPlayerListLength();
            return IsValidIndex(index) ? _players[index] : null;
        }

        public float GetCurrentTime(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.currentTime : 0f;
        }

        public float GetDuration(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.duration : 0f;
        }

        public float GetFrameRate(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.frameRate : 0f;
        }

        /// <summary>プレビュー表示用の動画テクスチャ。未読込時は null</summary>
        public Texture GetTexture(int index)
        {
            var player = GetPlayer(index);
            return player != null ? player.texture : null;
        }

        public bool RequiresVerticalFlip(int index)
        {
            var player = GetPlayer(index);
            return player != null && player.requiresVerticalFlip;
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
            _standaloneSettingsList.Clear();

            foreach (var src in timeline.videos)
            {
                var copy = new VideoSettings();
                copy.CopyFrom(src);
                _standaloneSettingsList.Add(copy);
            }

            if (_standaloneSettingsList.Count == 0)
            {
                _standaloneSettingsList.Add(new VideoSettings());
            }
        }

        /// <summary>
        /// プレイヤー側リストを settingsList の長さに合わせる。
        /// タイムライン読込で settingsList の実体が差し替わっても添字対応を保つため、
        /// 各操作の入口で呼ぶ。余った末尾のプレイヤーは破棄する
        /// </summary>
        private void SyncPlayerListLength()
        {
            var count = settingsList.Count;

            while (_players.Count > count)
            {
                var last = _players.Count - 1;
                DestroyPlayer(last);
                _players.RemoveAt(last);
                _loadedVideoPaths.RemoveAt(last);
                _loadedDisplayTypes.RemoveAt(last);
            }

            while (_players.Count < count)
            {
                _players.Add(null);
                _loadedVideoPaths.Add("");
                _loadedDisplayTypes.Add(VideoDisplayType.GUI);
            }
        }

        private void DestroyPlayer(int index)
        {
            if (_players[index] != null)
            {
                Object.Destroy(_players[index].gameObject);
                _players[index] = null;
            }
            _loadedVideoPaths[index] = "";
        }

        private void SetupImpl(int index)
        {
            var settings = GetSettings(index);

            if (_loadedDisplayTypes[index] != settings.displayType)
            {
                UnloadMovie(index);
                _loadedDisplayTypes[index] = settings.displayType;
            }

            if (!IsEnabled(index))
            {
                return;
            }

            if (_players[index] == null)
            {
                var guid = System.Guid.NewGuid().ToString();
                var gameObject = new GameObject("MoviePlayer_" + index + "_" + guid);
                var player = gameObject.AddComponent<MoviePlayerImpl>();
                player.Setup(settings);
                _players[index] = player;
            }
        }

        public void LoadMovie()
        {
            SyncPlayerListLength();

            for (var i = 0; i < settingsList.Count; i++)
            {
                LoadMovie(i);
            }
        }

        public void LoadMovie(int index)
        {
            SyncPlayerListLength();

            if (!IsValidIndex(index) || !IsEnabled(index))
            {
                return;
            }

            var path = GetSettings(index).path;
            if (_loadedVideoPaths[index] == path)
            {
                return;
            }
            _loadedVideoPaths[index] = path;

            SetupImpl(index);

            if (_players[index] != null)
            {
                _players[index].LoadMovie(path);
            }
        }

        public void UnloadMovie()
        {
            SyncPlayerListLength();

            for (var i = 0; i < _players.Count; i++)
            {
                DestroyPlayer(i);
            }
        }

        public void UnloadMovie(int index)
        {
            SyncPlayerListLength();

            if (IsValidIndex(index))
            {
                DestroyPlayer(index);
            }
        }

        public void ReloadMovie()
        {
            UnloadMovie();
            LoadMovie();
        }

        public void ReloadMovie(int index)
        {
            UnloadMovie(index);
            LoadMovie(index);
        }

        public void UpdateTransform()
        {
            ForEachPlayer(player => player.UpdateTransform());
        }

        public void UpdateTransform(int index)
        {
            WithPlayer(index, player => player.UpdateTransform());
        }

        public void UpdateVolume()
        {
            ForEachPlayer(player => player.UpdateVolume());
        }

        public void UpdateVolume(int index)
        {
            WithPlayer(index, player => player.UpdateVolume());
        }

        public void UpdateSpeed()
        {
            ForEachPlayer(player => player.UpdateSpeed());
        }

        public void UpdateSpeed(int index)
        {
            WithPlayer(index, player => player.UpdateSpeed());
        }

        public void UpdateSeekTime()
        {
            ForEachPlayer(player => player.UpdateSeekTime());
        }

        public void UpdateSeekTime(int index)
        {
            WithPlayer(index, player => player.UpdateSeekTime());
        }

        public void UpdateColor()
        {
            ForEachPlayer(player => player.UpdateColor());
        }

        public void UpdateColor(int index)
        {
            WithPlayer(index, player => player.UpdateColor());
        }

        public void UpdateMesh()
        {
            ForEachPlayer(player => player.UpdateMesh());
        }

        public void UpdateMesh(int index)
        {
            WithPlayer(index, player => player.UpdateMesh());
        }

        public void UpdateShader()
        {
            ForEachPlayer(player => player.UpdateShader());
        }

        public void UpdateShader(int index)
        {
            WithPlayer(index, player => player.UpdateShader());
        }

        private void ForEachPlayer(Action<MoviePlayerImpl> action)
        {
            SyncPlayerListLength();

            foreach (var player in _players)
            {
                if (player != null)
                {
                    action(player);
                }
            }
        }

        private void WithPlayer(int index, Action<MoviePlayerImpl> action)
        {
            var player = GetPlayer(index);
            if (player != null)
            {
                action(player);
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
