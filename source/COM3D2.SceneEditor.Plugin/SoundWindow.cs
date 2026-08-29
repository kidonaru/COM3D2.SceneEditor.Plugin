using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボイス・効果音の編集ウィンドウ。
    /// タイムラインのメイドボイス/効果音レイヤーから編集 UI を委譲された受け皿。
    /// 値はライブ状態 (maidCache / TimelineSeManager) を直接編集するため、
    /// キーフレーム登録はタイムライン操作ウィンドウ側で行えばそのままキー化される
    /// </summary>
    public class SoundWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903392;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "サウンド";

        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.TimelineSeManager seManager => MTEP.TimelineSeManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

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
            ボイス,
            効果音,
        }

        private TabType _tabType = TabType.ボイス;

        protected override void DrawMaidContent(Maid target)
        {
            _tabType = DrawInnerTabs(_tabType, 50);

            switch (_tabType)
            {
                case TabType.ボイス:
                    DrawVoice(view, target);
                    break;
                case TabType.効果音:
                    DrawSe(view);
                    break;
            }
        }

        /// <summary>メイドボイスレイヤーから移設した、ワンショット/ループボイスの編集</summary>
        private void DrawVoice(GUIView view, Maid target)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (timeline == null || maidCache == null)
            {
                view.DrawLabel("タイムラインとメイドの読込後に使用できます", -1, ROW_HEIGHT);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始",
                labelWidth = 30,
                min = 0f,
                max = timelineConfig.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceStartTime,
                onChanged = value => maidCache.oneShotVoiceStartTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "長さ",
                labelWidth = 30,
                min = 0f,
                max = timelineConfig.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceLength,
                onChanged = value => maidCache.oneShotVoiceLength = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "Fade",
                labelWidth = 30,
                min = 0f,
                max = timelineConfig.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0.1f,
                value = maidCache.voiceFadeTime,
                onChanged = value => maidCache.voiceFadeTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音程",
                labelWidth = 30,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = maidCache.voicePitch,
                onChanged = value => maidCache.voicePitch = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ボイス名",
                labelWidth = 75,
                value = maidCache.oneShotVoiceName,
                onChanged = value => maidCache.oneShotVoiceName = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ループボイス",
                labelWidth = 75,
                value = maidCache.loopVoiceName,
                onChanged = value => maidCache.loopVoiceName = value,
            });

            if (view.DrawButton("再生", 100, ROW_HEIGHT))
            {
                maidCache.PlayOneShotVoice();
            }

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

        // タイムライン固有の追加 SE と公式 SE を連結したコンボボックス用の一覧
        private readonly List<string> _seNames = new List<string>();

        private readonly GUIComboBox<string> _seNameComboBox = new GUIComboBox<string>
        {
            getName = (seName, index) => seName,
        };

        /// <summary>_seNames を構築した時点のタイムライン。切替の検出用に実体で持つ</summary>
        private MTEP.TimelineData _seNamesTimeline = null;

        private void UpdateSeNames(MTEP.TimelineData timeline)
        {
            _seNamesTimeline = timeline;
            _seNames.Clear();
            _seNames.AddRange(timeline.additionalSeNames);
            _seNames.AddRange(seManager.seNames);
        }

        private void DrawSeControl(GUIView view, MTEP.TimelineData timeline)
        {
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var updated = false;

            // 追加 SE はタイムラインごとの内容。切替時と件数の増減時に引き直す
            if (_seNamesTimeline != timeline ||
                _seNames.Count != timeline.additionalSeNames.Count + seManager.seNames.Count)
            {
                UpdateSeNames(timeline);
            }

            _seNameComboBox.items = _seNames;
            if (_seNameComboBox.currentItem != seManager.currentSeName)
            {
                _seNameComboBox.currentIndex = _seNameComboBox.items.IndexOf(seManager.currentSeName);
            }
            _seNameComboBox.onSelected = (seName, _) =>
            {
                seManager.currentSeName = seName;
                updated = true;
            };

            _seNameComboBox.DrawButton("SE名", view);

            updated |= view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = "再生間隔",
                    labelWidth = 60,
                    min = 0f,
                    max = timelineConfig.voiceMaxLength,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = seManager.currentInterval,
                    onChanged = value => seManager.currentInterval = value,
                });

            view.DrawToggle("ループ", seManager.currentIsLoop, 80, ROW_HEIGHT, newValue =>
            {
                seManager.currentIsLoop = newValue;
                updated = true;
            });

            view.BeginHorizontal();
            {
                if (view.DrawButton("再生", 100, ROW_HEIGHT))
                {
                    updated = true;
                }

                if (view.DrawButton("初期化", 100, ROW_HEIGHT))
                {
                    seManager.PlaySe("", 0f, false);
                }
            }
            view.EndLayout();

            if (updated)
            {
                seManager.StopSe();
                seManager.PlaySe(seManager.currentSeName, seManager.currentInterval, seManager.currentIsLoop);
            }
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
                            UpdateSeNames(timeline);
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
                UpdateSeNames(timeline);
            }

            _additionalSeName = "";
        }
    }
}
