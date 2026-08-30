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
    /// タイムラインの設定ウィンドウ。
    /// 「個別」タブは読み込み中のタイムライン (TimelineData) を、
    /// 「共通」タブはプラグイン共通の設定 (timelineConfig) を編集する。
    /// TimelineWindow のコントロールパネルの「設定」ボタンから開く
    /// </summary>
    public class TimelineSettingWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903385;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン設定";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int TAB_WIDTH = 60;
        /// <summary>横並びトグルの幅。ラベルが見切れない程度に固定する</summary>
        private static readonly int TOGGLE_WIDTH = 130;
        /// <summary>トラック 1 件分の行の高さ (名前行 + 範囲行 + 区切り線)</summary>
        private static readonly int TRACK_ROW_HEIGHT = 55;

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum SettingTabType
        {
            個別,
            共通,
            トラック,
        }

        private SettingTabType _tabType = SettingTabType.個別;

        private readonly GUIView _view = new GUIView();

        private static readonly string[] SingleFrameTypeNames = new string[]
        {
            "なし",
            "1F遅らせる",
            "1F早める",
        };

        private static readonly string[] VideoDisplayTypeNames = new string[]
        {
            "GUI",
            "3Dビュー",
            "最背面",
            "最前面",
        };

        private readonly GUIComboBox<MTEP.VideoDisplayType> _videoDisplayTypeComboBox = new GUIComboBox<MTEP.VideoDisplayType>
        {
            items = Enum.GetValues(typeof(MTEP.VideoDisplayType)).Cast<MTEP.VideoDisplayType>().ToList(),
            getName = (type, index) => VideoDisplayTypeNames[index],
            onSelected = (type, index) =>
            {
                if (timeline == null)
                {
                    return;
                }

                timeline.videoDisplayType = type;
                movieManager.ReloadMovie();
            },
        };

        private readonly GUIComboBox<MTEP.SingleFrameType> _singleFrameTypeComboBox = new GUIComboBox<MTEP.SingleFrameType>
        {
            items = Enum.GetValues(typeof(MTEP.SingleFrameType)).Cast<MTEP.SingleFrameType>().ToList(),
            getName = (type, index) => SingleFrameTypeNames[index],
            onSelected = (type, index) =>
            {
                if (timeline == null)
                {
                    return;
                }

                timeline.singleFrameType = type;
                timelineManager.ApplyCurrentFrame(true);
            },
        };

        private readonly GUIComboBox<MTEP.TangentType> _defaultTangentTypeComboBox = new GUIComboBox<MTEP.TangentType>
        {
            items = Enum.GetValues(typeof(MTEP.TangentType)).Cast<MTEP.TangentType>().ToList(),
            getName = (type, index) => MTEP.TangentData.TangentTypeNames[index],
            onSelected = (type, index) =>
            {
                timelineConfig.defaultTangentType = type;
                timelineConfig.dirty = true;
            },
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;
        private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;

        private static TimelineSettingWindow _instance = null;
        public static TimelineSettingWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineSettingWindow();
                }
                return _instance;
            }
        }

        private TimelineSettingWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineSettingPosX;
            y = config.timelineSettingPosY;
            width = config.timelineSettingWidth;
            height = config.timelineSettingHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineSettingPosX = x;
            config.timelineSettingPosY = y;
            config.timelineSettingWidth = width;
            config.timelineSettingHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineSettingVisible;
            set => config.timelineSettingVisible = value;
        }

        protected override void DrawContent()
        {
            DrawBody();

            // ボタン押下で登録されたフォーカスをポップアップへ引き渡す (TimelineWindow と同じ流儀)。
            // タイムライン未読込で早期 return しても飛ばさないよう、本体とは分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            // タブはスクロールビューの外に置き、どこまでスクロールしても切り替えられるようにする
            _tabType = _view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」になるため、
            // SettingWindow と同じく通常の行間に合わせて詰める
            _view.currentPos.y -= 5 + GUIView.defaultMargin;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            // トラック一覧 (DrawContentListView) は自前でスクロールするため、
            // 共有のスクロールビューには入れない (ネストしたスクロールは GUIView が非対応)
            if (_tabType == SettingTabType.トラック)
            {
                DrawTrackSetting(_view);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            switch (_tabType)
            {
                case SettingTabType.個別:
                    DrawSongSetting(_view);
                    break;
                case SettingTabType.共通:
                    DrawCommonSetting(_view);
                    break;
            }

            _view.EndScrollView();
        }

        /// <summary>個別設定 (読み込み中のタイムラインに紐づく設定) の描画</summary>
        private void DrawSongSetting(GUIView view)
        {
            view.DrawLabel("格納ディレクトリ名", -1, ROW_HEIGHT);
            view.DrawTextField(timeline.directoryName, -1, ROW_HEIGHT, newText => timeline.directoryName = newText);

            view.BeginHorizontal();
            {
                var newFrameRate = timeline.frameRate;

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "フレームレート",
                    value = timeline.frameRate,
                    width = 150,
                    height = ROW_HEIGHT,
                    onChanged = x => newFrameRate = x,
                });

                if (view.DrawButton("30", 30, ROW_HEIGHT))
                {
                    newFrameRate = 30;
                }

                if (view.DrawButton("60", 30, ROW_HEIGHT))
                {
                    newFrameRate = 60;
                }

                if (newFrameRate != timeline.frameRate)
                {
                    timeline.frameRate = newFrameRate;
                    timelineManager.ApplyCurrentFrame(true);
                }
            }
            view.EndLayout();

            _singleFrameTypeComboBox.currentIndex = (int)timeline.singleFrameType;
            _singleFrameTypeComboBox.DrawButton("1フレーム調整", view);

            view.BeginHorizontal();
            {
                view.DrawToggle("胸(左)の物理無効", timeline.useMuneKeyL, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timeline.useMuneKeyL = newValue;
                });

                view.DrawToggle("胸(右)の物理無効", timeline.useMuneKeyR, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timeline.useMuneKeyR = newValue;
                });
            }
            view.EndLayout();

            view.DrawToggle("ループアニメーション", timeline.isLoopAnm, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isLoopAnm = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });

            view.DrawHorizontalLine(Color.gray);

            DrawPostEffectSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawElementCountSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawLightToggleSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawBGMSetting(view);

            DrawVideoSetting(view);

            view.DrawHorizontalLine(Color.gray);

            if (view.DrawButton("個別設定を初期化", 130, ROW_HEIGHT))
            {
                MTEUtils.ShowConfirmDialog("個別設定を初期化しますか？", () =>
                {
                    if (timeline == null)
                    {
                        return;
                    }

                    timeline.ResetSettings();
                    timelineManager.Refresh();
                    timelineManager.ApplyCurrentFrame(true);
                }, null);
            }
        }

        /// <summary>ポストエフェクトの拡張と地面色の連動</summary>
        private void DrawPostEffectSection(GUIView view)
        {
            view.DrawToggle("ポストエフェクトの色拡張", timeline.usePostEffectExtraColor, -1, ROW_HEIGHT, newValue =>
            {
                timeline.usePostEffectExtraColor = newValue;
            });

            view.DrawToggle("ポストエフェクトのブレンド拡張", timeline.usePostEffectExtraBlend, -1, ROW_HEIGHT, newValue =>
            {
                timeline.usePostEffectExtraBlend = newValue;
            });

            view.DrawToggle("地面色表示を背景表示と連動", timeline.isGroundLinkedToBackground, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isGroundLinkedToBackground = newValue;
            });
        }

        /// <summary>
        /// タイムライン上の要素数 (テキスト/ポストエフェクト/サブカメラ) の増減。
        /// 旧レイヤー編集ウィンドウから移設
        /// </summary>
        private void DrawElementCountSection(GUIView view)
        {
            view.DrawLabel("要素数", 100, ROW_HEIGHT);

            DrawCountRow(view, "テキスト表示数", timeline.textCount, 1, 16,
                x => timeline.textCount = x);
            DrawCountRow(view, "パラフィン数", timeline.paraffinCount, 0, 8,
                x => timeline.paraffinCount = x);
            DrawCountRow(view, "距離フォグ数", timeline.distanceFogCount, 0, 4,
                x => timeline.distanceFogCount = x);
            DrawCountRow(view, "リムライト数", timeline.rimlightCount, 0, 8,
                x => timeline.rimlightCount = x);

            var subCameraManager = MTEP.SubCameraManager.instance;
            DrawCountRow(view, "サブカメラ数", subCameraManager.subCameras.Count,
                MTEP.SubCameraManager.MinSubCameraCount,
                MTEP.SubCameraManager.MaxSubCameraCount,
                x => subCameraManager.SetCameraCount(x));
        }

        /// <summary>
        /// ライトの補間まわりのトグル。旧レイヤー編集ウィンドウから移設。
        /// いずれもタイムラインに保存される個別設定のためこのタブに置く
        /// </summary>
        private void DrawLightToggleSection(GUIView view)
        {
            view.DrawToggle("ライトで色補間を有効化", timeline.isLightColorEasing, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isLightColorEasing = newValue;
            });

            view.DrawToggle("ライトで拡張補間を有効化", timeline.isLightExtraEasing, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isLightExtraEasing = newValue;
            });

            view.DrawToggle("ライトの互換性モードを有効化", timeline.isLightCompatibilityMode, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isLightCompatibilityMode = newValue;
                // SE では互換モードの実体がないためフラグの保存のみ行う
            });
        }

        /// <summary>整数値の増減行 (旧レイヤー編集ウィンドウの ±ボタン付き IntField と同型)</summary>
        private void DrawCountRow(
            GUIView view, string label, int value, int min, int max, Action<int> onChanged)
        {
            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel(label, view.labelWidth, ROW_HEIGHT);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = value,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = ROW_HEIGHT,
                    onChanged = x => onChanged(Mathf.Clamp(x, min, max)),
                });

                if (view.DrawButton("-", 20, ROW_HEIGHT, value > min))
                {
                    onChanged(value - 1);
                }
                if (view.DrawButton("+", 20, ROW_HEIGHT, value < max))
                {
                    onChanged(value + 1);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();
        }

        /// <summary>BGM の読み込みと BPM ライン表示 (MTE TimelineSettingUI から移植)</summary>
        private void DrawBGMSetting(GUIView view)
        {
            view.DrawLabel("BGM設定", 100, ROW_HEIGHT);

            view.BeginHorizontal();
            {
                view.DrawLabel("BGMパス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "BGMファイルを選択してください",
                        Filter = "音楽ファイル (*.wav;*.ogg)|*.wav;*.ogg",
                        InitialDirectory = timeline.bgmPath,
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        var path = openFileDialog.FileName;
                        timeline.bgmPath = path;
                        bgmManager.Load();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    bgmManager.Reload();
                }
            }
            view.EndLayout();

            view.DrawTextField(timeline.bgmPath, 240, ROW_HEIGHT, newText => timeline.bgmPath = newText);

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

            view.DrawToggle("BPMライン表示", timeline.isShowBPMLine, 120, ROW_HEIGHT, newValue =>
            {
                timeline.isShowBPMLine = newValue;
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "BPM",
                labelWidth = 50,
                min = 1,
                max = 300,
                step = 0.1f,
                defaultValue = 120,
                value = timeline.bpm,
                onChanged = value => timeline.bpm = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "オフセット",
                labelWidth = 50,
                min = -timeline.frameRate,
                max = timeline.frameRate,
                step = 0.1f,
                defaultValue = 0,
                value = timeline.bpmLineOffsetFrame,
                onChanged = value => timeline.bpmLineOffsetFrame = value,
            });

            view.AddSpace(10);
            view.DrawHorizontalLine(Color.gray);
        }

        /// <summary>動画の読み込みと表示形式ごとの配置調整 (MTE TimelineSettingUI から移植)</summary>
        private void DrawVideoSetting(GUIView view)
        {
            var isEnabled = timeline.videoEnabled;

            view.BeginHorizontal();
            {
                view.DrawLabel("動画設定", 100, ROW_HEIGHT);

                view.DrawToggle("有効", isEnabled, 60, ROW_HEIGHT, newValue =>
                {
                    timeline.videoEnabled = newValue;
                    if (newValue)
                    {
                        movieManager.LoadMovie();
                    }
                    else
                    {
                        movieManager.UnloadMovie();
                    }
                });
            }
            view.EndLayout();

            _videoDisplayTypeComboBox.currentIndex = (int)timeline.videoDisplayType;
            _videoDisplayTypeComboBox.DrawButton("表示形式", view);

            view.SetEnabled(isEnabled);

            view.BeginHorizontal();
            {
                view.DrawLabel("動画パス", 50, ROW_HEIGHT);

                if (view.DrawButton("選択", 50, ROW_HEIGHT))
                {
                    var openFileDialog = new WinFormsOpenFileDialog
                    {
                        Title = "動画ファイルを選択してください",
                        Filter = "動画ファイル (*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm)|*.mp4;*.avi;*.wmv;*.mov;*.flv;*.mkv;*.webm|すべてのファイル (*.*)|*.*",
                        InitialDirectory = timeline.videoPath
                    };

                    if (openFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                    {
                        var path = openFileDialog.FileName;
                        timeline.videoPath = path;
                        movieManager.LoadMovie();
                    }
                }

                if (view.DrawButton("再読込", 80, ROW_HEIGHT))
                {
                    movieManager.ReloadMovie();
                }
            }
            view.EndLayout();

            view.DrawTextField(timeline.videoPath, 240, ROW_HEIGHT, newText => timeline.videoPath = newText);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始位置",
                labelWidth = 60,
                min = -1f,
                max = movieManager.duration,
                step = movieManager.frameRate > 0f ? 1f / movieManager.frameRate : 0.01f,
                defaultValue = 0f,
                value = timeline.videoStartTime,
                onChanged = newValue =>
                {
                    timeline.videoStartTime = newValue;
                    movieManager.UpdateSeekTime();
                },
            });

            if (timeline.videoDisplayType == MTEP.VideoDisplayType.GUI)
            {
                var guiPosition = timeline.videoGUIPosition;
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
                    timeline.videoGUIPosition = newGUIPosition;
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
                    value = timeline.videoGUIScale,
                    onChanged = value =>
                    {
                        timeline.videoGUIScale = value;
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
                    value = timeline.videoGUIAlpha,
                    onChanged = value =>
                    {
                        timeline.videoGUIAlpha = value;
                        movieManager.UpdateColor();
                    },
                });
            }
            if (timeline.videoDisplayType == MTEP.VideoDisplayType.Mesh)
            {
                var position = timeline.videoPosition;
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
                    timeline.videoPosition = newPosition;
                    movieManager.UpdateTransform();
                }

                var rotation = MTEP.TransformDataBase.GetNormalizedEulerAngles(timeline.videoRotation);
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
                    timeline.videoRotation = newRotation;
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
                    value = timeline.videoScale,
                    onChanged = value =>
                    {
                        timeline.videoScale = value;
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
                    value = timeline.videoAlpha,
                    onChanged = value =>
                    {
                        timeline.videoAlpha = value;
                        movieManager.UpdateColor();
                    },
                });
            }
            if (timeline.videoDisplayType == MTEP.VideoDisplayType.Backmost)
            {
                var position = timeline.videoBackmostPosition;
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
                    timeline.videoBackmostPosition = newPosition;
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
                    value = timeline.videoBackmostScale,
                    onChanged = value =>
                    {
                        timeline.videoBackmostScale = value;
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
                    value = timeline.videoBackmostAlpha,
                    onChanged = value =>
                    {
                        timeline.videoBackmostAlpha = value;
                        movieManager.UpdateColor();
                    },
                });
            }
            if (timeline.videoDisplayType == MTEP.VideoDisplayType.Frontmost)
            {
                var position = timeline.videoFrontmostPosition;
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
                    timeline.videoFrontmostPosition = newPosition;
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
                    value = timeline.videoFrontmostScale,
                    onChanged = value =>
                    {
                        timeline.videoFrontmostScale = value;
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
                    value = timeline.videoFrontmostAlpha,
                    onChanged = value =>
                    {
                        timeline.videoFrontmostAlpha = value;
                        movieManager.UpdateColor();
                    },
                });
            }

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音量",
                labelWidth = 60,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0.5f,
                value = timeline.videoVolume,
                onChanged = newValue =>
                {
                    timeline.videoVolume = newValue;
                    movieManager.UpdateVolume();
                },
            });

            view.SetEnabled(true);
        }

        /// <summary>共通設定 (タイムライン全体で共有する設定) の描画</summary>
        private void DrawCommonSetting(GUIView view)
        {
            DrawCommonValueSection(view);
            DrawCommonToggleSection(view);
        }

        /// <summary>キーフレームの既定値と各種の編集レンジ</summary>
        private void DrawCommonValueSection(GUIView view)
        {
            _defaultTangentTypeComboBox.currentIndex = (int)timelineConfig.defaultTangentType;
            _defaultTangentTypeComboBox.DrawButton("初期補間曲線", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "移動範囲",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 100f,
                step = 0.1f,
                defaultValue = 5f,
                value = timelineConfig.positionRange,
                onChanged = value =>
                {
                    timelineConfig.positionRange = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "ボイス最大秒数",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 30f,
                step = 0f,
                defaultValue = 20f,
                value = timelineConfig.voiceMaxLength,
                onChanged = value =>
                {
                    timelineConfig.voiceMaxLength = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "動画先読み秒数",
                labelWidth = 100,
                width = -1,
                min = 0f,
                max = 1f,
                step = 0f,
                defaultValue = 0.5f,
                value = timelineConfig.videoPrebufferTime,
                onChanged = value =>
                {
                    timelineConfig.videoPrebufferTime = value;
                    timelineConfig.dirty = true;
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "背景透過度",
                labelWidth = 100,
                width = -1,
                min = 0f,
                max = 1f,
                step = 0f,
                defaultValue = 0.5f,
                value = timelineConfig.timelineBgAlpha,
                onChanged = value =>
                {
                    timelineConfig.timelineBgAlpha = value;
                    timelineConfig.dirty = true;
                },
            });

        }

        /// <summary>編集の挙動を切り替えるトグル群</summary>
        private void DrawCommonToggleSection(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawToggle("自動スクロール", timelineConfig.isAutoScroll, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.isAutoScroll = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("ポーズ履歴無効", timelineConfig.disablePoseHistory, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.disablePoseHistory = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("処理時間出力", timelineConfig.outputElapsedTime, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.outputElapsedTime = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();
        }

        /// <summary>トラック設定 (再生範囲の分割) の描画</summary>
        private void DrawTrackSetting(GUIView view)
        {
            if (view.DrawButton("追加", 80, ROW_HEIGHT))
            {
                timelineManager.AddTrack();
            }

            view.AddSpace(10);

            var tracks = timeline.tracks;
            if (tracks.Count == 0)
            {
                view.DrawLabel("トラックがありません", -1, ROW_HEIGHT);
                return;
            }

            // 行の内側で位置を決めるため、リスト側の余白は殺す。
            // padding は Init でリセットされないので、他タブへ持ち越さないよう必ず戻す
            view.padding = Vector2.zero;
            view.DrawContentListView(tracks, DrawTrack, -1, -1, TRACK_ROW_HEIGHT);
            view.padding = GUIView.defaultPadding;
        }

        /// <summary>トラック 1 件分の行。有効化トグル・名前・範囲・並べ替え・削除</summary>
        private void DrawTrack(GUIView view, MTEP.TrackData track, int index)
        {
            if (track == null)
            {
                return;
            }

            var width = view.viewRect.width;

            view.currentPos.x = 5;
            view.currentPos.y = 5;

            view.BeginHorizontal();
            {
                var isActive = timeline.activeTrack == track;

                view.DrawToggle("", isActive, 20, ROW_HEIGHT, newValue =>
                {
                    timelineManager.SetActiveTrack(track, !isActive);
                });

                // 並べ替えボタン (右端 30px) に被らない幅で名前欄を取る
                view.DrawTextField(track.name, width - 30 - view.currentPos.x, ROW_HEIGHT, newText =>
                {
                    track.name = newText;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("範囲", 40, ROW_HEIGHT);

                var updated = false;
                updated |= view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = track.startFrameNo,
                    width = 50,
                    height = ROW_HEIGHT,
                    onChanged = x => track.startFrameNo = x,
                });

                view.DrawLabel("～", 15, ROW_HEIGHT);

                updated |= view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = track.endFrameNo,
                    width = 50,
                    height = ROW_HEIGHT,
                    onChanged = x => track.endFrameNo = x,
                });

                if (view.DrawButton("削除", 50, ROW_HEIGHT))
                {
                    timelineManager.RemoveTrack(track);
                }

                // 再生中のトラックの範囲を変えたときだけ、その場で再生位置へ反映する
                if (updated && track == timeline.activeTrack)
                {
                    timelineManager.ApplyCurrentFrame(true);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);

            view.BeginLayout(GUIView.LayoutDirection.Free);
            {
                view.currentPos.x = width - 30;
                view.currentPos.y = 5;

                if (view.DrawButton("∧", 20, ROW_HEIGHT))
                {
                    timelineManager.MoveUpTrack(track);
                }

                view.currentPos.y += 25;
                if (view.DrawButton("∨", 20, ROW_HEIGHT))
                {
                    timelineManager.MoveDownTrack(track);
                }
            }
            view.EndLayout();
        }
    }
}
