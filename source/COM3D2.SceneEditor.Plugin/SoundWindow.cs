using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
// UnityEngine と同名型 (Screen 等) の衝突を避けるため WinForms はエイリアスで参照する
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// BGM・ボイス・効果音の編集ウィンドウ。
    /// ボイス/効果音タブはタイムラインのメイドボイス/効果音レイヤーから編集 UI を
    /// 委譲された受け皿で、値はライブ状態 (maidCache / TimelineSeManager) を直接
    /// 編集するため、キーフレーム登録はタイムライン操作ウィンドウ側で行えばそのまま
    /// キー化される。BGM タブはタイムラインに依存せず単独で利用できる
    /// </summary>
    public class SoundWindow : MaidWindowBase
    {
        // 統合前の BGM ウィンドウの ID を引き継ぐ。ロック状態は ID 単位で永続化されるため
        public static readonly int WINDOW_ID = 8903374;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "サウンド";

        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
        private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;
        private static MTEP.TimelineData timeline => MTEP.TimelineManager.instance.timeline;

        private static SoundWindow _instance = null;
        public static SoundWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SoundWindow();
                }
                return _instance;
            }
        }

        private SoundWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.soundPosX;
            y = config.soundPosY;
            width = config.soundWidth;
            height = config.soundHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.soundPosX = x;
            config.soundPosY = y;
            config.soundWidth = width;
            config.soundHeight = height;
        }

        public override bool savedVisible
        {
            get => config.soundVisible;
            set => config.soundVisible = value;
        }

        private enum TabType
        {
            BGM,
            ボイス,
            効果音,
        }

        private TabType _tabType = TabType.BGM;

        /// <summary>
        /// 対象メイドを使うのはボイスタブだけなので、基底の共通描画は切る。
        /// タブ行を最上部に置くため、選択行は DrawVoice の先頭で自前に描く
        /// </summary>
        protected override bool showMaidSelector => false;

        // showMaidSelector が false のため target は使わない。対象は DrawVoice 内で取り直す
        protected override void DrawMaidContent(Maid target)
        {
            _tabType = DrawInnerTabs(_tabType, 50);

            switch (_tabType)
            {
                case TabType.BGM:
                    DrawBgm(view);
                    break;
                case TabType.ボイス:
                    DrawVoice(view);
                    break;
                case TabType.効果音:
                    DrawSe(view);
                    break;
            }
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                BgmUtils.EnsureSoundDataLoaded();
            }
        }

        private string _bgmSearchText = "";

        /// <summary>
        /// ゲーム BGM の一覧表示・再生・停止と、タイムライン BGM ファイルの設定。
        /// 一覧はフォトモードの PhotoSoundData、再生は SoundMgr.PlayBGM の同一経路を使う
        /// </summary>
        private void DrawBgm(GUIView view)
        {
            if (!BgmUtils.EnsureSoundDataLoaded())
            {
                view.DrawLabel("BGM一覧を取得できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            var playingFileName = BgmUtils.GetPlayingFileName();

            DrawCurrentBgmRow(view, playingFileName);
            view.DrawHorizontalLine();
            DrawBgmFileSection(view);
            view.DrawHorizontalLine();
            view.DrawTextField("検索", LABEL_WIDTH, _bgmSearchText, -1, ROW_HEIGHT,
                value => _bgmSearchText = value);
            DrawBgmList(view, playingFileName);
        }

        /// <summary>
        /// タイムライン BGM ファイルの設定 (タイムライン設定ウィンドウから移設)。
        /// 値は BGMManager.settings に入るため、タイムライン未読込でも編集できる。
        /// 未読込時はタイムライン再生に追随しないため手動の再生/停止ボタンを出す
        /// </summary>
        private void DrawBgmFileSection(GUIView view)
        {
            var settings = bgmManager.settings;

            view.DrawLabel("BGMファイル", 100, ROW_HEIGHT);

            view.BeginHorizontal();
            {
                view.DrawLabel("パス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "BGMファイルを選択してください",
                        Filter = "音楽ファイル (*.wav;*.ogg)|*.wav;*.ogg",
                        InitialDirectory = settings.bgmPath,
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        settings.bgmPath = openFileDialog.FileName;
                        bgmManager.Load();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    bgmManager.Reload();
                }
            }
            view.EndLayout();

            view.DrawTextField(settings.bgmPath, 240, ROW_HEIGHT, newText => settings.bgmPath = newText);

            if (timeline == null)
            {
                view.BeginHorizontal();
                {
                    view.SetEnabled(bgmManager.IsLoaded());
                    if (view.DrawButton("再生", 60, ROW_HEIGHT))
                    {
                        bgmManager.Play();
                    }
                    if (view.DrawButton("一時停止", 80, ROW_HEIGHT))
                    {
                        bgmManager.Pause();
                    }
                    if (view.DrawButton("停止", 60, ROW_HEIGHT))
                    {
                        bgmManager.Stop();
                    }
                    view.SetEnabled(true);
                    view.DrawLabel("タイムライン読込後は再生に追随します", -1, ROW_HEIGHT, textColor: Color.gray);
                }
                view.EndLayout();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音量",
                labelWidth = 50,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 100,
                step = 0,
                defaultValue = 100,
                value = bgmManager.volumeDance,
                onChanged = value =>
                {
                    bgmManager.volumeDance = (int)value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawToggle("BPMライン表示", settings.isShowBPMLine, 120, ROW_HEIGHT, newValue =>
            {
                settings.isShowBPMLine = newValue;
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "BPM",
                labelWidth = 50,
                min = 1,
                max = 300,
                step = 0.1f,
                defaultValue = 120,
                value = settings.bpm,
                onChanged = value => settings.bpm = value,
            });

            // オフセットの範囲はフレームレート依存。未読込時は既定の 30 を使う
            var frameRate = timeline != null ? timeline.frameRate : 30f;
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "オフセット",
                labelWidth = 50,
                min = -frameRate,
                max = frameRate,
                step = 0.1f,
                defaultValue = 0,
                value = settings.bpmLineOffsetFrame,
                onChanged = value => settings.bpmLineOffsetFrame = value,
            });
        }

        /// <summary>再生中の曲名と停止ボタンの行</summary>
        private void DrawCurrentBgmRow(GUIView view, string playingFileName)
        {
            if (playingFileName == null)
            {
                view.DrawLabel("再生していません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            view.BeginHorizontal();
            {
                if (view.DrawButton("停止", 60, ROW_HEIGHT))
                {
                    BgmUtils.Stop();
                }

                // 幅 -1 は行の残り全部を取るため、固定幅のボタンより後に描く
                // 一覧にない曲 (イベントBGM等) はファイル名をそのまま出す
                var soundData = PhotoSoundData.Get(playingFileName);
                view.DrawLabel(soundData != null ? soundData.name : playingFileName,
                    -1, ROW_HEIGHT, Color.cyan);
            }
            view.EndLayout();
        }

        /// <summary>フィルタ適用済みの BGM ボタン一覧。再生中の曲はシアン表示</summary>
        private void DrawBgmList(GUIView view, string playingFileName)
        {
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var soundData in PhotoSoundData.data)
            {
                if (!string.IsNullOrEmpty(_bgmSearchText) &&
                    soundData.name.IndexOf(_bgmSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var isPlaying = soundData.file_name == playingFileName;
                if (view.DrawButton(soundData.name, -1, ROW_HEIGHT, true,
                    isPlaying ? Color.cyan : Color.white))
                {
                    // 再生中の曲を押した場合も再生し直す (頭出しとして働く)
                    soundData.Play();
                }
            }

            view.EndScrollView();
        }

        /// <summary>メイドボイスレイヤーから移設した、ワンショット/ループボイスの編集</summary>
        private void DrawVoice(GUIView view)
        {
            var target = DrawMaidSelector(view);

            var timeline = MTEP.TimelineManager.instance.timeline;
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (timeline == null || maidCache == null)
            {
                view.DrawLabel("タイムラインとメイドの読込後に使用できます", -1, ROW_HEIGHT);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            VoiceRowDrawer.Draw(view, maidCache, ROW_HEIGHT);

            view.SetEnabled(view.focusedComboBox == null);

            view.DrawHorizontalLine(Color.gray);
        }

        private enum SeTabType
        {
            操作,
            管理,
        }

        private SeTabType _seTabType = SeTabType.操作;

        /// <summary>効果音レイヤーから移設した SE の操作・管理</summary>
        private void DrawSe(GUIView view)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            if (timeline == null)
            {
                // 間欠再生の駆動元 (TimelineSeManager.Update) もタイムライン有効時のみ回るため条件を揃える
                view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            _seTabType = DrawInnerTabs(_seTabType, 50);

            switch (_seTabType)
            {
                case SeTabType.操作:
                    DrawSeControl(view, timeline);
                    break;
                case SeTabType.管理:
                    DrawSeManage(view, timeline);
                    break;
            }
        }

        // SE の操作行は 1 つしか出さないので、行ドロワーも 1 つで足りる
        private readonly SeRowDrawer _seRowDrawer = new SeRowDrawer();

        private void DrawSeControl(GUIView view, MTEP.TimelineData timeline)
        {
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            _seRowDrawer.Draw(view, timeline, ROW_HEIGHT);
        }

        private string _additionalSeName = "";

        private void DrawSeManage(GUIView view, MTEP.TimelineData timeline)
        {
            view.SetEnabled(view.focusedComboBox == null);

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "SE名",
                labelWidth = 50,
                value = _additionalSeName,
                onChanged = value => _additionalSeName = value,
            });

            if (view.DrawButton("追加", 100, ROW_HEIGHT))
            {
                AddSe(timeline);
            }

            view.DrawHorizontalLine(Color.gray);

            view.BeginScrollView();
            {
                for (var i = 0; i < timeline.additionalSeNames.Count; i++)
                {
                    var seName = timeline.additionalSeNames[i];

                    view.BeginHorizontal();
                    {
                        view.DrawLabel(seName, view.viewRect.width - 50 - 10, ROW_HEIGHT);

                        if (view.DrawButton("削除", 50, ROW_HEIGHT))
                        {
                            timeline.additionalSeNames.Remove(seName);
                            SeRowDrawer.InvalidateSeNames();
                            break;
                        }
                    }
                    view.EndLayout();
                }
            }
            view.EndScrollView();
        }

        private void AddSe(MTEP.TimelineData timeline)
        {
            if (_additionalSeName == "")
            {
                return;
            }

            if (!_additionalSeName.EndsWith(".ogg"))
            {
                _additionalSeName += ".ogg";
            }

            if (!GameUty.FileSystem.IsExistentFile(_additionalSeName))
            {
                MTEUtils.ShowDialog($"ファイルが存在しません\n{_additionalSeName}");
                return;
            }

            if (!timeline.additionalSeNames.Contains(_additionalSeName))
            {
                timeline.additionalSeNames.Add(_additionalSeName);
                SeRowDrawer.InvalidateSeNames();
            }

            _additionalSeName = "";
        }
    }
}
