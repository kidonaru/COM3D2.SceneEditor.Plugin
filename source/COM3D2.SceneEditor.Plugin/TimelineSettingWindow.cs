using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

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

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum SettingTabType
        {
            個別,
            共通,
        }

        private SettingTabType _tabType = SettingTabType.個別;

        private readonly GUIView _view = new GUIView();

        private static readonly string[] SingleFrameTypeNames = new string[]
        {
            "なし",
            "1F遅らせる",
            "1F早める",
        };

        private readonly GUIComboBox<Maid.EyeMoveType> _eyeMoveTypeComboBox = new GUIComboBox<Maid.EyeMoveType>
        {
            items = Enum.GetValues(typeof(Maid.EyeMoveType)).Cast<Maid.EyeMoveType>().ToList(),
            getName = (type, index) => type.ToString(),
            // 選択確定は ComboBoxPopupWindow 側で後から呼ばれるため、
            // 開いている間にタイムラインが閉じられた場合に備えて null を弾く
            onSelected = (type, index) =>
            {
                if (timeline == null)
                {
                    return;
                }

                timeline.eyeMoveType = type;
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

        private readonly GUIComboBox<MTEP.MoveEasingType> _defaultEasingTypeComboBox = new GUIComboBox<MTEP.MoveEasingType>
        {
            // Max は要素数を表す番兵で、選ぶとイージング関数の添字が範囲外になるため候補から外す
            items = Enum.GetValues(typeof(MTEP.MoveEasingType)).Cast<MTEP.MoveEasingType>()
                .Where(type => type != MTEP.MoveEasingType.Max).ToList(),
            getName = (type, index) => type.ToString(),
            onSelected = (type, index) =>
            {
                timelineConfig.defaultEasingType = type;
                timelineConfig.dirty = true;
            },
        };

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

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

            _eyeMoveTypeComboBox.currentIndex = (int)timeline.eyeMoveType;
            _eyeMoveTypeComboBox.DrawButton("メイド目線", view);

            _singleFrameTypeComboBox.currentIndex = (int)timeline.singleFrameType;
            _singleFrameTypeComboBox.DrawButton("1フレーム調整", view);

            view.DrawToggle("顔/瞳の固定化", timeline.useHeadKey, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
            {
                timeline.useHeadKey = newValue;
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

            view.DrawToggle("イージングを次のキーフレームに適用", timeline.isEasingAppliedToNextKeyframe, -1, ROW_HEIGHT, newValue =>
            {
                timeline.isEasingAppliedToNextKeyframe = newValue;
                timelineManager.ApplyCurrentFrame(true);
            });

            view.DrawHorizontalLine(Color.gray);

            DrawTangentSection(view);

            view.DrawHorizontalLine(Color.gray);

            DrawPostEffectSection(view);

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

        /// <summary>レイヤー種別ごとのタンジェント補間の有効化</summary>
        private void DrawTangentSection(GUIView view)
        {
            view.DrawLabel("タンジェント補間", -1, ROW_HEIGHT);

            DrawTangentToggle(view, "カメラ", timeline.isTangentCamera,
                newValue => timeline.isTangentCamera = newValue, typeof(MTEP.CameraTimelineLayer));

            DrawTangentToggle(view, "ライト", timeline.isTangentLight,
                newValue => timeline.isTangentLight = newValue, typeof(MTEP.LightTimelineLayer));

            DrawTangentToggle(view, "メイド移動", timeline.isTangentMove,
                newValue => timeline.isTangentMove = newValue, typeof(MTEP.MoveTimelineLayer));

            DrawTangentToggle(view, "モデル", timeline.isTangentModel,
                newValue => timeline.isTangentModel = newValue, typeof(MTEP.ModelTimelineLayer));

            DrawTangentToggle(view, "モデルボーン", timeline.isTangentModelBone,
                newValue => timeline.isTangentModelBone = newValue, typeof(MTEP.ModelBoneTimelineLayer));

            DrawTangentToggle(view, "モデルシェイプ", timeline.isTangentModelShapeKey,
                newValue => timeline.isTangentModelShapeKey = newValue, typeof(MTEP.ModelShapeKeyTimelineLayer));
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
        /// タンジェント補間トグル 1 行。
        /// 切り替え時は対象レイヤーのタンジェントを作り直して現在フレームを再適用する
        /// </summary>
        private void DrawTangentToggle(
            GUIView view, string label, bool value, Action<bool> setValue, Type layerType)
        {
            view.DrawToggle(label, value, -1, ROW_HEIGHT, newValue =>
            {
                setValue(newValue);

                foreach (var targetLayer in timelineManager.FindLayers(layerType))
                {
                    targetLayer.InitTangent();
                    targetLayer.ApplyCurrentFrame(true);
                }
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

            _defaultEasingTypeComboBox.currentIndex = (int)timelineConfig.defaultEasingType;
            _defaultEasingTypeComboBox.DrawButton("初期イージング", view);

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
                label = "拡縮範囲",
                labelWidth = 100,
                width = -1,
                min = 1f,
                max = 10f,
                step = 0.1f,
                defaultValue = 5f,
                value = timelineConfig.scaleRange,
                onChanged = value =>
                {
                    timelineConfig.scaleRange = value;
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
                view.DrawToggle("自動揺れボーン", timelineConfig.isAutoYureBone, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.isAutoYureBone = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("常にIKを表示", timelineConfig.alwaysShowIK, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.alwaysShowIK = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("色をHSVで指定", timelineConfig.useHSVColor, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.useHSVColor = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("処理時間出力", timelineConfig.outputElapsedTime, TOGGLE_WIDTH, ROW_HEIGHT, newValue =>
                {
                    timelineConfig.outputElapsedTime = newValue;
                    timelineConfig.dirty = true;
                });
            }
            view.EndLayout();
        }
    }
}
