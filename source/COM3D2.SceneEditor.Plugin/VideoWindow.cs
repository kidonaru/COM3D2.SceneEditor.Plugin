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

        /// <summary>スライダー・座標行のラベル幅</summary>
        private static readonly int LABEL_WIDTH = 60;

        // 1px ドラッグあたりの増減量 (InspectorWindow と揃える)
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        /// <summary>GUI 表示位置は -1〜1 の狭い範囲なので、3D 位置より細かく動かす</summary>
        private const float NormalizedPositionSensitivity = 0.001f;

        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "GUI",
            "3Dビュー",
            "最背面",
            "最前面",
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
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

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawVideoSetting(_view);

            _view.EndScrollView();

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

            view.DrawTextField(settings.path, -1, ROW_HEIGHT, newText => settings.path = newText);

            if (timeline == null)
            {
                view.DrawLabel("タイムライン読込後にシークと再生速度が同期します", -1, ROW_HEIGHT, textColor: Color.gray);
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始位置",
                labelWidth = LABEL_WIDTH,
                width = -1,
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
                labelWidth = LABEL_WIDTH,
                width = -1,
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

        /// <summary>
        /// 表示位置の 1 行 (InspectorWindow と同じ XY 横並びの数値入力)。
        /// スライダーだと軸ごとに 1 行を使い、表示形式の切替で行数が大きく変わって読みづらいため
        /// </summary>
        private void DrawPositionRow(
            GUIView view,
            Vector2 value,
            Vector2 defaultValue,
            Action<Vector2> onChanged)
        {
            view.DrawVector2Row(new GUIView.Vector2RowOption
            {
                label = "位置",
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                dragSensitivity = NormalizedPositionSensitivity,
                value = value,
                onChanged = onChanged,
                onReset = () => onChanged(defaultValue),
            });
        }

        private void DrawGuiSetting(GUIView view)
        {
            DrawPositionRow(view, settings.guiPosition, Vector2.zero, newValue =>
            {
                settings.guiPosition = newValue;
                movieManager.UpdateTransform();
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = LABEL_WIDTH,
                width = -1,
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
                labelWidth = LABEL_WIDTH,
                width = -1,
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
            Vector3RowDrawer.Draw(view, "位置", PositionSensitivity, LABEL_WIDTH, ROW_HEIGHT,
                settings.position,
                newValue =>
                {
                    settings.position = newValue;
                    movieManager.UpdateTransform();
                },
                () =>
                {
                    settings.position = Vector3.zero;
                    movieManager.UpdateTransform();
                });

            Vector3RowDrawer.Draw(view, "回転", RotationSensitivity, LABEL_WIDTH, ROW_HEIGHT,
                MTEP.TransformDataBase.GetNormalizedEulerAngles(settings.rotation),
                newValue =>
                {
                    settings.rotation = newValue;
                    movieManager.UpdateTransform();
                },
                () =>
                {
                    settings.rotation = Vector3.zero;
                    movieManager.UpdateTransform();
                });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = LABEL_WIDTH,
                width = -1,
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
                labelWidth = LABEL_WIDTH,
                width = -1,
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
            DrawPositionRow(view, settings.backmostPosition, Vector2.zero, newValue =>
            {
                settings.backmostPosition = newValue;
                movieManager.UpdateMesh();
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = LABEL_WIDTH,
                width = -1,
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
                labelWidth = LABEL_WIDTH,
                width = -1,
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

        /// <summary>最前面表示の既定位置 (VideoSettings.frontmostPosition の初期値と揃える)</summary>
        private static readonly Vector2 FrontmostDefaultPosition = new Vector2(-0.8f, 0.8f);

        private void DrawFrontmostSetting(GUIView view)
        {
            DrawPositionRow(view, settings.frontmostPosition, FrontmostDefaultPosition, newValue =>
            {
                settings.frontmostPosition = newValue;
                movieManager.UpdateMesh();
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "表示サイズ",
                labelWidth = LABEL_WIDTH,
                width = -1,
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
                labelWidth = LABEL_WIDTH,
                width = -1,
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
