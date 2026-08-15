using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// BGM の一覧表示・再生・停止を行うウィンドウ。
    /// 一覧はフォトモードの PhotoSoundData、再生は SoundMgr.PlayBGM の同一経路を使う
    /// </summary>
    public class BgmWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903374;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "BGM";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;

        private string _searchText = "";

        private readonly GUIView _view = new GUIView();

        private static BgmWindow _instance = null;
        public static BgmWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BgmWindow();
                }
                return _instance;
            }
        }

        private BgmWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.bgmPosX;
            y = config.bgmPosY;
            width = config.bgmWidth;
            height = config.bgmHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.bgmPosX = x;
            config.bgmPosY = y;
            config.bgmWidth = width;
            config.bgmHeight = height;
        }

        public override bool savedVisible
        {
            get => config.bgmVisible;
            set => config.bgmVisible = value;
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                BgmUtils.EnsureSoundDataLoaded();
            }
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            if (!BgmUtils.EnsureSoundDataLoaded())
            {
                _view.DrawLabel("BGM一覧を取得できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            var playingFileName = BgmUtils.GetPlayingFileName();

            DrawCurrentBgmRow(playingFileName);
            _view.DrawHorizontalLine();
            _view.DrawTextField("検索", LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value => _searchText = value);
            DrawBgmList(playingFileName);
        }

        /// <summary>再生中の曲名と停止ボタンの行</summary>
        private void DrawCurrentBgmRow(string playingFileName)
        {
            if (playingFileName == null)
            {
                _view.DrawLabel("再生していません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("停止", 60, ROW_HEIGHT))
                {
                    BgmUtils.Stop();
                }

                // 幅 -1 は行の残り全部を取るため、固定幅のボタンより後に描く
                // 一覧にない曲 (イベントBGM等) はファイル名をそのまま出す
                var soundData = PhotoSoundData.Get(playingFileName);
                _view.DrawLabel(soundData != null ? soundData.name : playingFileName,
                    -1, ROW_HEIGHT, Color.cyan);
            }
            _view.EndLayout();
        }

        /// <summary>フィルタ適用済みの BGM ボタン一覧。再生中の曲はシアン表示</summary>
        private void DrawBgmList(string playingFileName)
        {
            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var soundData in PhotoSoundData.data)
            {
                if (!string.IsNullOrEmpty(_searchText) &&
                    soundData.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var isPlaying = soundData.file_name == playingFileName;
                if (_view.DrawButton(soundData.name, -1, ROW_HEIGHT, true,
                    isPlaying ? Color.cyan : Color.white))
                {
                    // 再生中の曲を押した場合も再生し直す (頭出しとして働く)
                    soundData.Play();
                }
            }

            _view.EndScrollView();
        }
    }
}
