using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
// UnityEngine と同名型 (Screen 等) の衝突を避けるため WinForms はエイリアスで参照する
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 動画の読み込みと表示形式ごとの配置調整ウィンドウ (タイムライン設定ウィンドウから移設)。
    /// 値は MovieManager.settings に入るためタイムライン未読込でも編集できる。
    /// 未読込時はループ再生のみで、シークや再生速度のタイムライン同期は読込後に働く
    /// </summary>
    public class VideoWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903396;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "動画";

        private static readonly int ROW_HEIGHT = 20;

        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "GUI",
            "3Dビュー",
            "最背面",
            "最前面",
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;
        private static MTEP.VideoSettings settings => movieManager.settings;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private readonly GUIComboBox<MTEP.VideoDisplayType> _videoDisplayTypeComboBox = new GUIComboBox<MTEP.VideoDisplayType>
        {
            items = Enum.GetValues(typeof(MTEP.VideoDisplayType)).Cast<MTEP.VideoDisplayType>().ToList(),
            getName = (type, index) => VideoDisplayTypeNames[index],
            onSelected = (type, index) =>
            {
                settings.displayType = type;
                movieManager.ReloadMovie();
            },
        };

        private static VideoWindow _instance = null;
        public static VideoWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VideoWindow();
                }
                return _instance;
            }
        }

        private VideoWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.videoPosX;
            y = config.videoPosY;
            width = config.videoWidth;
            height = config.videoHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.videoPosX = x;
            config.videoPosY = y;
            config.videoWidth = width;
            config.videoHeight = height;
        }

        public override bool savedVisible
        {
            get => config.videoVisible;
            set => config.videoVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            _view.SetEnabled(_view.focusedComboBox == null);
            DrawVideoSetting(_view);

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawVideoSetting(GUIView view)
        {
            var isEnabled = settings.enabled;

            view.DrawToggle("有効", isEnabled, 60, ROW_HEIGHT, newValue =>
            {
                settings.enabled = newValue;
                if (newValue)
                {
                    movieManager.LoadMovie();
                }
                else
                {
                    movieManager.UnloadMovie();
                }
            });

            _videoDisplayTypeComboBox.currentIndex = (int)settings.displayType;
            _videoDisplayTypeComboBox.DrawButton("表示形式", view);

            view.SetEnabled(isEnabled && view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.DrawLabel("動画パス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "動画ファイルを選択してください",
                        Filter = "動画ファイル (*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm)|*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm|すべてのファイル (*.*)|*.*",
                        InitialDirectory = settings.path
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        settings.path = openFileDialog.FileName;
                        movieManager.LoadMovie();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    movieManager.ReloadMovie();
                }
            }
            view.EndLayout();

            view.DrawTextField(settings.path, 240, ROW_HEIGHT, newText => settings.path = newText);

            if (timeline == null)
            {
                view.DrawLabel("タイムライン読込後にシークと再生速度が同期します", -1, ROW_HEIGHT, textColor: Color.gray);
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始位置",
                labelWidth = 60,
                min = -1f,
                max = movieManager.duration,
                step = movieManager.frameRate > 0f ? 1f / movieManager.frameRate : 0.01f,
                defaultValue = 0f,
                value = settings.startTime,
                onChanged = newValue =>
                {
                    settings.startTime = newValue;
                    movieManager.UpdateSeekTime();
                },
            });

            switch (settings.displayType)
            {
                case MTEP.VideoDisplayType.GUI:
                    DrawGuiSetting(view);
                    break;
                case MTEP.VideoDisplayType.Mesh:
                    DrawMeshSetting(view);
                    break;
                case MTEP.VideoDisplayType.Backmost:
                    DrawBackmostSetting(view);
                    break;
                case MTEP.VideoDisplayType.Frontmost:
                    DrawFrontmostSetting(view);
                    break;
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音量",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0.5f,
                value = settings.volume,
                onChanged = newValue =>
                {
                    settings.volume = newValue;
                    movieManager.UpdateVolume();
                },
            });

            view.SetEnabled(true);
        }

        private void DrawGuiSetting(GUIView view)
        {
            var guiPosition = settings.guiPosition;
            var newGUIPosition = guiPosition;
            for (var i = 0; i < 2; i++)
            {
                var value = guiPosition[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newGUIPosition[i] = newValue,
                });
            }

            if (newGUIPosition != guiPosition)
            {
                settings.guiPosition = newGUIPosition;
                movieManager.UpdateTransform();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.guiScale,
                onChanged = value =>
                {
                    settings.guiScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.guiAlpha,
                onChanged = value =>
                {
                    settings.guiAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawMeshSetting(GUIView view)
        {
            var position = settings.position;
            var newPosition = position;
            for (var i = 0; i < 3; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -timelineConfig.positionRange,
                    max = timelineConfig.positionRange,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.position = newPosition;
                movieManager.UpdateTransform();
            }

            var rotation = MTEP.TransformDataBase.GetNormalizedEulerAngles(settings.rotation);
            var newRotation = rotation;
            for (var i = 0; i < 3; i++)
            {
                var value = rotation[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.RotationNames[i],
                    labelWidth = 60,
                    min = -180f,
                    max = 180f,
                    step = 1f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newRotation[i] = newValue,
                });
            }

            if (newRotation != rotation)
            {
                settings.rotation = newRotation;
                movieManager.UpdateTransform();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 5f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.scale,
                onChanged = value =>
                {
                    settings.scale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.alpha,
                onChanged = value =>
                {
                    settings.alpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawBackmostSetting(GUIView view)
        {
            var position = settings.backmostPosition;
            var newPosition = position;
            for (var i = 0; i < 2; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -2f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.backmostPosition = newPosition;
                movieManager.UpdateMesh();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 2f,
                step = 0.1f,
                defaultValue = 1f,
                value = settings.backmostScale,
                onChanged = value =>
                {
                    settings.backmostScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0.5f,
                value = settings.backmostAlpha,
                onChanged = value =>
                {
                    settings.backmostAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }

        private void DrawFrontmostSetting(GUIView view)
        {
            var position = settings.frontmostPosition;
            var newPosition = position;
            for (var i = 0; i < 2; i++)
            {
                var value = position[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = MTEP.TransformDataBase.PositionNames[i],
                    labelWidth = 60,
                    min = -2f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = i == 0 ? -0.8f : 0.8f,
                    value = value,
                    onChanged = newValue => newPosition[i] = newValue,
                });
            }

            if (newPosition != position)
            {
                settings.frontmostPosition = newPosition;
                movieManager.UpdateMesh();
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = 60,
                min = 0f,
                max = 2f,
                step = 0.1f,
                defaultValue = 0.38f,
                value = settings.frontmostScale,
                onChanged = value =>
                {
                    settings.frontmostScale = value;
                    movieManager.UpdateTransform();
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "透過度",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = settings.frontmostAlpha,
                onChanged = value =>
                {
                    settings.frontmostAlpha = value;
                    movieManager.UpdateColor();
                },
            });
        }
    }
}
