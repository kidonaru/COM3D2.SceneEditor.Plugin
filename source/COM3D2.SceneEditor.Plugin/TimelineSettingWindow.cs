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

        private static readonly string[] MotionLayerFocusWindowNames =
        {
            "モーション",
            "IK",
        };

        private readonly GUIComboBox<MTEP.MotionLayerFocusWindow> _motionLayerFocusWindowComboBox
            = new GUIComboBox<MTEP.MotionLayerFocusWindow>
        {
            items = Enum.GetValues(typeof(MTEP.MotionLayerFocusWindow)).Cast<MTEP.MotionLayerFocusWindow>().ToList(),
            getName = (type, index) => MotionLayerFocusWindowNames[index],
            onSelected = (type, index) =>
            {
                timelineConfig.motionLayerFocusWindow = type;
                timelineConfig.dirty = true;
            },
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;
        private static MTEP.CameraManager cameraManager => MTEP.CameraManager.instance;

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
            view.DrawToggle("1フレーム調整を anm にも適用", timeline.isSingleFrameAnm, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isSingleFrameAnm = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });

            view.DrawToggle("表情をタンジェント補間", timeline.isTangentFace, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isTangentFace = newValue;

                // OFF で保存された XML はタンジェントを持たないので、未編集のときだけ既定へ戻す
                // (編集済みのタンジェントは ON/OFF を往復しても保持する)
                if (newValue)
                {
                    MTEP.FaceTangentToggle.ResetIfUntouched(timeline.layers);
                }
                timelineManager.ApplyCurrentFrame(true);
            });

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

            DrawLetterBoxSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawGroundLinkSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawImageOutputSection(view);

            view.DrawHorizontalLine(Color.gray);

            // サムネは保存時に未作成の場合しか撮られないので、撮り直しはここから行う
            if (view.DrawButton("サムネイル更新", 130, ROW_HEIGHT) && timelineManager.SaveThumbnail())
            {
                // 撮り直したサムネを一覧へ反映する
                TimelineLoadManager.Reload();
            }

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

        /// <summary>アスペクト比とレターボックス透過度 (幅か高さが 0 なら LetterBoxView 側で非表示になる)</summary>
        private void DrawLetterBoxSection(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("アスペクト比", 70, ROW_HEIGHT);

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "幅",
                    labelWidth = 30,
                    value = timeline.aspectWidth,
                    width = 80,
                    height = ROW_HEIGHT,
                    onChanged = x => timeline.aspectWidth = x,
                });

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "高さ",
                    labelWidth = 30,
                    value = timeline.aspectHeight,
                    width = 80,
                    height = ROW_HEIGHT,
                    onChanged = x => timeline.aspectHeight = x,
                });
            }
            view.EndLayout();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "ﾚﾀｰﾎﾞｯｸｽ透過度",
                labelWidth = 100,
                min = 0f,
                max = 1f,
                defaultValue = 1f,
                value = timeline.letterBoxAlpha,
                onChanged = value =>
                {
                    timeline.letterBoxAlpha = value;
                    cameraManager.ResetCache();
                },
            });
        }

        /// <summary>連番画像出力の設定 (MTE の TimelineSettingUI から移植)</summary>
        private void DrawImageOutputSection(GUIView view)
        {
            view.DrawLabel("連番画像出力設定", -1, ROW_HEIGHT);

            view.BeginHorizontal();
            {
                var newFrameRate = timeline.imageOutputFrameRate;

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "フレームレート",
                    value = timeline.imageOutputFrameRate,
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

                if (newFrameRate != timeline.imageOutputFrameRate)
                {
                    timeline.imageOutputFrameRate = newFrameRate;
                }
            }
            view.EndLayout();

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "出力名",
                labelWidth = 50,
                value = timeline.imageOutputFormat,
                onChanged = value => timeline.imageOutputFormat = value,
                hiddenButton = true,
            });

            view.BeginHorizontal();
            {
                view.DrawLabel("画像サイズ", 70, ROW_HEIGHT);

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "幅",
                    labelWidth = 30,
                    fieldType = FloatFieldType.Int,
                    value = timeline.imageOutputSize.x,
                    width = 80,
                    height = ROW_HEIGHT,
                    onChanged = x => timeline.imageOutputSize.x = x,
                });

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    label = "高さ",
                    labelWidth = 30,
                    fieldType = FloatFieldType.Int,
                    value = timeline.imageOutputSize.y,
                    width = 80,
                    height = ROW_HEIGHT,
                    onChanged = x => timeline.imageOutputSize.y = x,
                });
            }
            view.EndLayout();

            var enabled = MTEP.PluginUtils.IsExistsImageOutputDirPath(timeline.anmName);
            if (view.DrawButton("画像出力先を開く", 120, ROW_HEIGHT, enabled))
            {
                MTEUtils.OpenDirectory(MTEP.PluginUtils.GetImageOutputDirPath(timeline.anmName));
            }
        }

        /// <summary>地面色と背景表示の連動</summary>
        private void DrawGroundLinkSection(GUIView view)
        {
            view.DrawToggle("地面色表示を背景表示と連動", timeline.isGroundLinkedToBackground, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isGroundLinkedToBackground = newValue;
            });
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

            // メイドアニメレイヤーの選択で前面へ出すウィンドウ (モーション / IK)
            _motionLayerFocusWindowComboBox.currentIndex = (int)timelineConfig.motionLayerFocusWindow;
            _motionLayerFocusWindowComboBox.DrawButton("アニメ選択で前面", view);

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
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                // タイムラインのレイヤー表示モード。ON でカテゴリ内の全レイヤー、OFF でアクティブレイヤーのみを出す
                var isCategoryMode = timelineConfig.layerViewMode == MTEP.TimelineLayerViewMode.Category;
                view.DrawToggle("カテゴリ表示", isCategoryMode, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.layerViewMode = newValue
                        ? MTEP.TimelineLayerViewMode.Category
                        : MTEP.TimelineLayerViewMode.Layer;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("手首・足首を指グループに表示", timelineConfig.isWristAnkleInFingerMenu, -1, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.isWristAnkleInFingerMenu = newValue;
                    timelineConfig.dirty = true;
                    foreach (var layer in timelineManager.layers.OfType<MTEP.MotionTimelineLayer>())
                    {
                        layer.RebuildMenuItems();
                    }
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
                    timelineManager.RequestHistory("トラック変更");
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
                if (updated)
                {
                    if (track == timeline.activeTrack)
                    {
                        timelineManager.ApplyCurrentFrame(true);
                    }
                    timelineManager.RequestHistory("トラック変更");
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
