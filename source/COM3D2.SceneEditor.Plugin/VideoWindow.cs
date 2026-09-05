using System;
using System.Collections.Generic;
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
        /// <summary>最背面・最前面の位置は -1〜1 の狭い範囲なので、3D 位置より細かく動かす</summary>
        private const float NormalizedPositionSensitivity = 0.001f;

        // enum 名 (GUI) は XML 互換のため据え置き、表示名だけ実態に合わせる
        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "プレビュー",
            "3Dビュー",
            "最背面",
            "最前面",
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;

        /// <summary>操作対象の動画添字</summary>
        private int _videoIndex = 0;

        private MTEP.VideoSettings settings => movieManager.GetSettings(_videoIndex);

        private readonly GUIComboBox<int> _videoComboBox = new GUIComboBox<int>
        {
            // 表示は 1 始まり、内部添字は 0 始まり (VideoPreviewWindow のタイトルも同じ規約)
            getName = (index, _) => "動画" + (index + 1),
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 120),
        };

        /// <summary>コンボ選択肢 (0〜videoCount-1)。本数変更時だけ作り直す</summary>
        private readonly List<int> _videoIndexItems = new List<int>();

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private readonly GUIComboBox<MTEP.VideoDisplayType> _videoDisplayTypeComboBox = new GUIComboBox<MTEP.VideoDisplayType>
        {
            items = Enum.GetValues(typeof(MTEP.VideoDisplayType)).Cast<MTEP.VideoDisplayType>().ToList(),
            getName = (type, index) => VideoDisplayTypeNames[index],
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
            // インスタンスメンバー (settings) を参照するため、フィールド初期化子ではなくここで設定する
            _videoComboBox.onSelected = (index, _) => _videoIndex = index;
            _videoDisplayTypeComboBox.onSelected = (type, index) =>
            {
                settings.displayType = type;
                movieManager.ReloadMovie(_videoIndex);
            };
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

            DrawVideoSelector(_view);
            if (movieManager.IsValidIndex(_videoIndex))
            {
                DrawVideoSetting(_view);
            }

            _view.EndScrollView();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>動画数の増減行と操作対象コンボ</summary>
        private void DrawVideoSelector(GUIView view)
        {
            CountRowDrawer.Draw(view, "動画数", ROW_HEIGHT, movieManager.videoCount,
                MTEP.MovieManager.MinVideoCount,
                MTEP.MovieManager.MaxVideoCount,
                count => movieManager.videoCount = count);

            var videoCount = movieManager.videoCount;
            _videoIndex = Mathf.Clamp(_videoIndex, 0, videoCount - 1);

            if (_videoIndexItems.Count != videoCount)
            {
                _videoIndexItems.Clear();
                for (var i = 0; i < videoCount; i++)
                {
                    _videoIndexItems.Add(i);
                }
            }

            _videoComboBox.items = _videoIndexItems;
            _videoComboBox.currentIndex = _videoIndex;
            _videoComboBox.DrawButton("操作対象", view);
        }

        private void DrawVideoSetting(GUIView view)
        {
            var isEnabled = settings.enabled;

            view.BeginHorizontal();
            {
                view.DrawToggle("有効", isEnabled, 60, ROW_HEIGHT, newValue =>
                {
                    settings.enabled = newValue;
                    if (newValue)
                    {
                        movieManager.LoadMovie(_videoIndex);
                    }
                    else
                    {
                        movieManager.UnloadMovie(_videoIndex);
                    }
                });

                // プレビューウィンドウの表示切替。操作対象の動画に対応する 1 枚を開閉する
                var previewWindow = VideoPreviewWindow.GetInstance(_videoIndex);
                view.DrawToggle("プレビュー", previewWindow.isShowWnd, 90, ROW_HEIGHT, _ =>
                {
                    WindowManager.ToggleWindowVisible(previewWindow);
                });
            }
            view.EndLayout();

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
                        movieManager.LoadMovie(_videoIndex);
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    movieManager.ReloadMovie(_videoIndex);
                }
            }
            view.EndLayout();

            view.DrawTextField(settings.path, -1, ROW_HEIGHT, newText => settings.path = newText);

            if (timeline == null)
            {
                view.DrawLabel("タイムライン読込後にシークと再生速度が同期します", -1, ROW_HEIGHT, textColor: Color.gray);
            }

            DrawStartTimeAndVolumeRow(view);

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

            view.SetEnabled(true);
        }

        /// <summary>2 値を 1 行に並べるときの数値入力欄の幅</summary>
        private static readonly int PAIR_FIELD_WIDTH = 60;

        /// <summary>
        /// 開始位置と音量の 1 行。どちらも 1 値なのでスライダーで 2 行使うより、
        /// ドラッグ数値入力を横に並べたほうが表示形式ごとの設定行を見渡しやすい
        /// </summary>
        private void DrawStartTimeAndVolumeRow(GUIView view)
        {
            var frameRate = movieManager.GetFrameRate(_videoIndex);
            var duration = movieManager.GetDuration(_videoIndex);

            view.BeginHorizontal();
            {
                view.DrawDragFloatField(new GUIView.DragFloatFieldOption
                {
                    label = "開始位置",
                    labelWidth = LABEL_WIDTH,
                    fieldWidth = PAIR_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    // 1px で 1 フレーム動かす。メタデータ未確定時は秒単位の細かさで代替する
                    dragSensitivity = frameRate > 0f ? 1f / frameRate : 0.01f,
                    value = settings.startTime,
                    minValue = -1f,
                    maxValue = duration > 0f ? duration : float.MaxValue,
                    onChanged = newValue =>
                    {
                        settings.startTime = newValue;
                        movieManager.UpdateSeekTime(_videoIndex);
                    },
                    onReset = () =>
                    {
                        settings.startTime = 0f;
                        movieManager.UpdateSeekTime(_videoIndex);
                    },
                });

                view.DrawDragFloatField(new GUIView.DragFloatFieldOption
                {
                    label = "音量",
                    labelWidth = LABEL_WIDTH,
                    fieldWidth = PAIR_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    value = settings.volume,
                    minValue = 0f,
                    maxValue = 1f,
                    onChanged = newValue =>
                    {
                        settings.volume = newValue;
                        movieManager.UpdateVolume(_videoIndex);
                    },
                    onReset = () =>
                    {
                        settings.volume = 0f;
                        movieManager.UpdateVolume(_videoIndex);
                    },
                });
            }
            view.EndLayout();
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

        /// <summary>
        /// プレビュー形式の設定。値はプレビューウィンドウが毎フレーム直接読むため、
        /// 他の表示形式と違って変更後の反映呼び出しは要らない
        /// </summary>
        private void DrawGuiSetting(GUIView view)
        {
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
                onChanged = value => settings.guiScale = value,
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
                onChanged = value => settings.guiAlpha = value,
            });
        }

        private void DrawMeshSetting(GUIView view)
        {
            Vector3RowDrawer.Draw(view, "位置", PositionSensitivity, LABEL_WIDTH, ROW_HEIGHT,
                settings.position,
                newValue =>
                {
                    settings.position = newValue;
                    movieManager.UpdateTransform(_videoIndex);
                },
                () =>
                {
                    settings.position = Vector3.zero;
                    movieManager.UpdateTransform(_videoIndex);
                });

            Vector3RowDrawer.Draw(view, "回転", RotationSensitivity, LABEL_WIDTH, ROW_HEIGHT,
                MTEP.TransformDataBase.GetNormalizedEulerAngles(settings.rotation),
                newValue =>
                {
                    settings.rotation = newValue;
                    movieManager.UpdateTransform(_videoIndex);
                },
                () =>
                {
                    settings.rotation = Vector3.zero;
                    movieManager.UpdateTransform(_videoIndex);
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
                    movieManager.UpdateTransform(_videoIndex);
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
                    movieManager.UpdateColor(_videoIndex);
                },
            });
        }

        private void DrawBackmostSetting(GUIView view)
        {
            DrawPositionRow(view, settings.backmostPosition, Vector2.zero, newValue =>
            {
                settings.backmostPosition = newValue;
                movieManager.UpdateMesh(_videoIndex);
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
                    movieManager.UpdateTransform(_videoIndex);
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
                    movieManager.UpdateColor(_videoIndex);
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
                movieManager.UpdateMesh(_videoIndex);
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
                    movieManager.UpdateTransform(_videoIndex);
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
                    movieManager.UpdateColor(_videoIndex);
                },
            });
        }
    }
}
