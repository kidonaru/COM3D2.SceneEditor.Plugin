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
            onSelected = (type, index) =>
            {
                timeline.eyeMoveType = type;
            },
        };

        private readonly GUIComboBox<MTEP.SingleFrameType> _singleFrameTypeComboBox = new GUIComboBox<MTEP.SingleFrameType>
        {
            items = Enum.GetValues(typeof(MTEP.SingleFrameType)).Cast<MTEP.SingleFrameType>().ToList(),
            getName = (type, index) => SingleFrameTypeNames[index],
            onSelected = (type, index) =>
            {
                timeline.singleFrameType = type;
                timelineManager.ApplyCurrentFrame(true);
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

            view.DrawHorizontalLine(Color.gray);

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

            view.DrawHorizontalLine(Color.gray);

            if (view.DrawButton("個別設定を初期化", 130, ROW_HEIGHT))
            {
                MTEUtils.ShowConfirmDialog("個別設定を初期化しますか？", () =>
                {
                    timeline.ResetSettings();
                    timelineManager.Refresh();
                    timelineManager.ApplyCurrentFrame(true);
                }, null);
            }
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
        }
    }
}
