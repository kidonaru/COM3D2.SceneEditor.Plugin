using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作ウィンドウ。TimelineWindow 上段にあった操作パネル
    /// (ファイル/フレーム操作/キーフレーム/範囲操作/操作対象/表示トグル) を独立させたもの
    /// </summary>
    public class TimelineControlWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903391;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン操作";
        protected override int minWidth => 300;
        protected override int minHeight => 190;

        private static TimelineControlWindow _instance = null;
        public static TimelineControlWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineControlWindow();
                }
                return _instance;
            }
        }

        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.StudioHackBase studioHack => studioHackManager.studioHack;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static string anmName
        {
            get => timeline.anmName;
            set => timeline.anmName = value;
        }

        private readonly GUIView _view = new GUIView();

        /// <summary>範囲操作の開始フレーム。TimelineWindow が範囲選択表示に参照する</summary>
        public int selectStartFrameNo = 0;
        /// <summary>範囲操作の終了フレーム。TimelineWindow が範囲選択表示に参照する</summary>
        public int selectEndFrameNo = 0;

        private TimelineControlWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineControlPosX;
            y = config.timelineControlPosY;
            width = config.timelineControlWidth;
            height = config.timelineControlHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineControlPosX = x;
            config.timelineControlPosY = y;
            config.timelineControlWidth = width;
            config.timelineControlHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineControlVisible;
            set => config.timelineControlVisible = value;
        }

        private enum FileMenuType
        {
            New,
            OutputAnm,
        }

        private readonly GUIComboBox<FileMenuType> fileMenuComboBox = new GUIComboBox<FileMenuType>
        {
            defaultName = "ファイル",
            items = new List<FileMenuType>
            {
                FileMenuType.New,
                FileMenuType.OutputAnm,
            },
            getName = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.New:
                        return "新規作成";
                    case FileMenuType.OutputAnm:
                        return "アニメ出力";
                    default:
                        return "";
                }
            },
            getEnabled = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.OutputAnm:
                        return timelineManager.IsValidData();
                    default:
                        return true;
                }
            },
            onSelected = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.New:
                        timelineManager.CreateNewTimeline();
                        break;
                    case FileMenuType.OutputAnm:
                        timelineManager.OutputAnm();
                        break;
                }
            },
            showArrow = false,
            buttonSize = new Vector2(60, 20),
        };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            onSelected = (maidCache, index) =>
            {
                maidManager.ChangeMaid(maidCache.maid);
            },
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private const float ROW_HEIGHT = 20f;

        /// <summary>外周の余白。既定値より詰めて 1 行に並ぶ要素数を稼ぐ</summary>
        private static readonly Vector2 CONTENT_PADDING = new Vector2(3, 3);

        /// <summary>グループ見出しラベルの幅</summary>
        private const float GROUP_LABEL_WIDTH = 80f;

        /// <summary>文字数が少ない見出しの幅 (範囲操作)</summary>
        private const float SHORT_LABEL_WIDTH = 55f;

        /// <summary>再生速度の既定値 (R ボタンで戻す)</summary>
        private const float DEFAULT_ANM_SPEED = 1f;

        /// <summary>S / E ドラッグラベルと入力欄の幅</summary>
        private const float RANGE_LABEL_WIDTH = 15f;
        private const float RANGE_FIELD_WIDTH = 50f;

        /// <summary>状態メッセージの最小幅 (これを確保できなければ折り返す) と最大幅</summary>
        private const float STATUS_MESSAGE_MIN_WIDTH = 200f;
        private const float STATUS_MESSAGE_MAX_WIDTH = 400f;

        /// <summary>
        /// 次の要素が右端を超える場合に折り返す
        /// (TimelineTemplateWindow のテンプレボタンと同じ流儀)
        /// </summary>
        private static void WrapIfNeeded(GUIView view, float width)
        {
            if (view.currentPos.x + width > view.viewRect.width - view.padding.x * 2)
            {
                view.EndLayout();
                view.BeginHorizontal();
            }
        }

        /// <summary>
        /// グループの見出しラベル。見出しだけが行末に取り残されないよう、
        /// 直後の要素 (nextWidth) と一緒に折り返す
        /// </summary>
        private static void DrawGroupLabel(GUIView view, string label, float nextWidth, float labelWidth = GROUP_LABEL_WIDTH)
        {
            WrapIfNeeded(view, labelWidth + GUIView.defaultMargin + nextWidth);
            view.DrawLabel(label, labelWidth, ROW_HEIGHT);
        }

        protected override void DrawContent()
        {
            DrawBody();

            // 早期 return してもコンボのポップアップ処理を飛ばさないよう本体と分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            var local = ToLocalRect(contentRect);
            var view = _view;

            view.Init(local);

            // 要素数が多いウィンドウなので、旧レイアウトと同じく外周の余白を詰める
            view.padding = CONTENT_PADDING;

            if (studioHack == null)
            {
                view.DrawLabel("シーンが有効ではありません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.BeginScrollView();
            {
                // 全要素を 1 つの横並びに流し込み、右端で折り返させる
                view.BeginHorizontal();
                {
                    DrawControls(view);
                }
                view.EndLayout();
            }
            view.EndScrollView();
        }

        private void DrawControls(GUIView view)
        {
            var isStudioHackValid = studioHack.IsValid();
            var isMaidValid = maidManager.IsValid();

            var editEnabled = isMaidValid
                            && isStudioHackValid
                            && timeline != null
                            && maidManager.maid != null;

            DrawFileMenu(view, editEnabled);
            DrawStatusMessage(view, isStudioHackValid, isMaidValid);

            if (!editEnabled)
            {
                return;
            }

            DrawFrameControls(view);
            DrawKeyFrameControls(view);
            DrawRangeControls(view);
            DrawTargetMaidControls(view);
            DrawToggles(view);
        }

        private void DrawFileMenu(GUIView view, bool editEnabled)
        {
            WrapIfNeeded(view, 60);
            fileMenuComboBox.currentIndex = -1;
            fileMenuComboBox.DrawButton(view);

            WrapIfNeeded(view, 60);
            if (view.DrawButton("セーブ", 60, ROW_HEIGHT, editEnabled))
            {
                OnSaveClicked();
            }

            // MTE のロードボタンと同様に開く動作のみ (他ボタンと違いトグルしない)
            WrapIfNeeded(view, 60);
            if (view.DrawButton("ロード", 60, ROW_HEIGHT))
            {
                OnLoadClicked();
            }

            // 要素数 (テキスト・エフェクト・サブカメラ) の増減もここから辿れるようにする
            WrapIfNeeded(view, 60);
            if (view.DrawButton("設定", 60, ROW_HEIGHT))
            {
                WindowManager.ToggleWindowVisible(TimelineSettingWindow.instance);
            }
        }

        /// <summary>
        /// スクロールビューの EndScrollView を飛ばさないよう、
        /// 中断する早期 return はハンドラ側に閉じ込める (ロードも同様)
        /// </summary>
        private void OnSaveClicked()
        {
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
            if (!timelineManager.IsValidData())
            {
                MTEUtils.ShowDialog(timelineManager.errorMessage);
                return;
            }
            timelineManager.SaveTimeline();

            // 保存したタイムラインをサムネ付きで一覧へ出す
            TimelineLoadManager.Reload();
        }

        private void OnLoadClicked()
        {
            if (!studioHack.IsValid())
            {
                MTEUtils.ShowDialog(studioHack.errorMessage);
                return;
            }
            if (!TimelineLoadWindow.instance.isShowWnd)
            {
                WindowManager.ToggleWindowVisible(TimelineLoadWindow.instance);
            }
        }

        /// <summary>状態メッセージ。狭いウィンドウでもはみ出さないよう残り幅に収める</summary>
        private void DrawStatusMessage(GUIView view, bool isStudioHackValid, bool isMaidValid)
        {
            WrapIfNeeded(view, STATUS_MESSAGE_MIN_WIDTH);

            var remainWidth = view.viewRect.width - view.currentPos.x - view.padding.x * 2;
            var width = Mathf.Min(STATUS_MESSAGE_MAX_WIDTH, remainWidth);

            if (!isStudioHackValid)
            {
                view.DrawLabel(studioHack.errorMessage, width, ROW_HEIGHT, Color.yellow);
            }
            else if (!isMaidValid)
            {
                view.DrawLabel(maidManager.errorMessage, width, ROW_HEIGHT, Color.yellow);
            }
            else if (!timelineManager.IsValidData())
            {
                view.DrawLabel(timelineManager.errorMessage, width, ROW_HEIGHT, Color.yellow);
            }
            else if (studioHackManager.isPoseEditing)
            {
                var keyName = timelineConfig.GetKeyName(MTEP.KeyBindType.AddKeyFrame);
                view.DrawLabel("[" + keyName + "]キーでキーフレームを登録します", width, ROW_HEIGHT, Color.white);
            }
            else
            {
                var keyName = timelineConfig.GetKeyName(MTEP.KeyBindType.EditMode);
                view.DrawLabel("[" + keyName + "]キーで編集モードに切り替えます", width, ROW_HEIGHT, Color.white);
            }
        }

        private void DrawFrameControls(GUIView view)
        {
            WrapIfNeeded(view, 260);
            view.DrawTextField("アニメ名", 60, anmName, 260, ROW_HEIGHT, newText => anmName = newText);

            WrapIfNeeded(view, 75 + GUIView.defaultMargin + 60);
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "最終フレーム",
                labelWidth = 75,
                value = timeline.maxFrameNo,
                fieldWidth = 60,
                height = ROW_HEIGHT,
                onChanged = value => timelineManager.SetMaxFrameNo(value),
            });

            var newFrameNo = timelineManager.currentFrameNo;

            // シークボタン群 (|< .< < [num] > >. >|) は分断すると操作しにくいため見出しごとまとめて折り返す
            DrawGroupLabel(view, "フレーム操作", 25 * 6 + 50);
            view.margin = 0;
            {
                if (view.DrawButton("|<", 25, ROW_HEIGHT))
                {
                    newFrameNo = 0;
                }
                if (view.DrawRepeatButton(".<", 25, ROW_HEIGHT))
                {
                    var prevFrame = timelineManager.GetPrevFrame(newFrameNo);
                    if (prevFrame != null)
                    {
                        newFrameNo = prevFrame.frameNo;
                    }
                }
                if (view.DrawRepeatButton("<", 25, ROW_HEIGHT))
                {
                    newFrameNo--;
                }

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = newFrameNo,
                    width = 50,
                    height = ROW_HEIGHT,
                    onChanged = value => newFrameNo = value,
                });

                if (view.DrawRepeatButton(">", 25, ROW_HEIGHT))
                {
                    newFrameNo++;
                }
                if (view.DrawRepeatButton(">.", 25, ROW_HEIGHT))
                {
                    var nextFrame = timelineManager.GetNextFrame(newFrameNo);
                    if (nextFrame != null)
                    {
                        newFrameNo = nextFrame.frameNo;
                    }
                }
                if (view.DrawButton(">|", 25, ROW_HEIGHT))
                {
                    newFrameNo = timeline.maxFrameNo;
                }
            }
            view.margin = GUIView.defaultMargin;

            if (newFrameNo != timelineManager.currentFrameNo)
            {
                timelineManager.SeekCurrentFrame(newFrameNo);
                TimelineWindow.instance.FixScrollPosition();
            }

            WrapIfNeeded(view, 20);
            if (currentLayer.isAnmPlaying)
            {
                if (view.DrawButton("■", 20, ROW_HEIGHT))
                {
                    timelineManager.Pause();
                }
            }
            else
            {
                if (view.DrawButton("▶", 20, ROW_HEIGHT))
                {
                    timelineManager.Play();
                }
            }

            WrapIfNeeded(view, 60 + GUIView.defaultMargin + 50 + GUIView.ResetButtonWidth);
            view.DrawDragFloatField(new GUIView.DragFloatFieldOption
            {
                label = "再生速度",
                labelWidth = 60,
                value = timelineManager.anmSpeed,
                minValue = 0f,
                maxValue = 2f,
                fieldWidth = 50,
                height = ROW_HEIGHT,
                onChanged = value => timelineManager.anmSpeed = value,
                onReset = () => timelineManager.anmSpeed = DEFAULT_ANM_SPEED,
            });
        }

        private void DrawKeyFrameControls(GUIView view)
        {
            DrawGroupLabel(view, "キーフレーム", 50);
            if (view.DrawButton("登録", 50, ROW_HEIGHT, studioHackManager.isPoseEditing))
            {
                currentLayer.AddKeyFrameDiff();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("全登録", 60, ROW_HEIGHT))
            {
                currentLayer.AddKeyFrameAll();
            }

            WrapIfNeeded(view, 50);
            if (view.DrawButton("削除", 50, ROW_HEIGHT, timelineManager.HasSelected()))
            {
                timelineManager.RemoveSelectedFrame();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("コピー", 60, ROW_HEIGHT, timelineManager.HasSelected()))
            {
                timelineManager.CopyFramesToClipboard();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ペースト", 60, ROW_HEIGHT))
            {
                timelineManager.PasteFramesFromClipboard(false);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("反転P", 60, ROW_HEIGHT))
            {
                timelineManager.PasteFramesFromClipboard(true);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ポーズC", 60, ROW_HEIGHT))
            {
                timelineManager.CopyPoseToClipboard();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ポーズP", 60, ROW_HEIGHT, studioHackManager.isPoseEditing))
            {
                timelineManager.PastePoseFromClipboard();
            }
        }

        private void DrawRangeControls(GUIView view)
        {
            // 開始 (S) ～終了 (E) の入力欄とリセットは見出しごとまとめて折り返す (要素間 margin 4 つ分を含む)
            var rangeFieldWidth = RANGE_LABEL_WIDTH + GUIView.defaultMargin + RANGE_FIELD_WIDTH;
            DrawGroupLabel(view, "範囲操作",
                rangeFieldWidth * 2 + 20 + GUIView.defaultMargin * 4, SHORT_LABEL_WIDTH);

            // S / E はラベルドラッグでも増減できる
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "S",
                labelWidth = RANGE_LABEL_WIDTH,
                value = selectStartFrameNo,
                fieldWidth = RANGE_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value => selectStartFrameNo = value,
            });

            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "E",
                labelWidth = RANGE_LABEL_WIDTH,
                value = selectEndFrameNo,
                fieldWidth = RANGE_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value => selectEndFrameNo = value,
            });

            if (view.DrawButton("R", 20, ROW_HEIGHT))
            {
                selectStartFrameNo = 0;
                selectEndFrameNo = 0;
            }

            var isValidRange = timelineManager.IsValidFrameRnage(selectStartFrameNo, selectEndFrameNo);

            WrapIfNeeded(view, 65);
            if (view.DrawButton("範囲選択", 65, ROW_HEIGHT))
            {
                timelineManager.SelectFramesRange(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ挿入", 65, ROW_HEIGHT, isValidRange && selectStartFrameNo > 0))
            {
                timelineManager.InsertFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ削除", 65, ROW_HEIGHT, isValidRange && selectStartFrameNo > 0))
            {
                timelineManager.DeleteFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ複製", 65, ROW_HEIGHT, isValidRange))
            {
                timelineManager.DuplicateFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("縦選択", 60, ROW_HEIGHT, !timelineConfig.isEasyEdit))
            {
                timelineManager.SelectVerticalBones();
            }
        }

        /// <summary>操作対象メイドの選択。レイヤー選択自体は TimelineWindow のボーンメニュー上部にある</summary>
        private void DrawTargetMaidControls(GUIView view)
        {
            if (!currentLayer.hasSlotNo)
            {
                return;
            }

            // 操作対象ラベルとメイドコンボはまとめて折り返す
            WrapIfNeeded(view, 60 + 150);
            view.DrawLabel("操作対象", 60, ROW_HEIGHT);

            _maidComboBox.currentIndex = currentLayer.slotNo;
            _maidComboBox.items = maidManager.maidCaches;
            _maidComboBox.DrawButton(view);
        }

        private void DrawToggles(GUIView view)
        {
            WrapIfNeeded(view, 80);
            view.DrawToggle("簡易表示", timelineConfig.isEasyEdit, 80, ROW_HEIGHT, newValue =>
            {
                timelineConfig.isEasyEdit = newValue;
                timelineConfig.dirty = true;
                timelineManager.Refresh();
            });

            WrapIfNeeded(view, 80);
            view.DrawToggle("編集モード", studioHackManager.isPoseEditing, 80, ROW_HEIGHT, newValue =>
            {
                studioHackManager.isPoseEditing = newValue;
            });

            WrapIfNeeded(view, 80);
            view.DrawToggle("自動登録", timelineConfig.isAutoKeyFrame, 80, ROW_HEIGHT, newValue =>
            {
                timelineConfig.isAutoKeyFrame = newValue;
                timelineConfig.dirty = true;
            });

            WrapIfNeeded(view, 80);
            view.DrawToggle("メイド表示", maidManager.maid.Visible, 80, ROW_HEIGHT, newValue =>
            {
                maidManager.maid.Visible = newValue;
            });

            WrapIfNeeded(view, 80);
            view.DrawToggle("モデル表示", modelManager.Visible, 80, ROW_HEIGHT, newValue =>
            {
                modelManager.Visible = newValue;
            });

            WrapIfNeeded(view, 80);
            view.DrawToggle("背景表示", timeline.isBackgroundVisible, 80, ROW_HEIGHT, newValue =>
            {
                timeline.isBackgroundVisible = newValue;
            });

            if (timelineManager.hasCameraLayer)
            {
                var cameraUpdated = false;
                WrapIfNeeded(view, 80);
                cameraUpdated |= view.DrawToggle("カメラ同期", timelineConfig.isCameraSync, 80, ROW_HEIGHT, !currentLayer.isCameraLayer, newValue =>
                {
                    timelineConfig.isCameraSync = newValue;
                    timelineConfig.dirty = true;
                });

                WrapIfNeeded(view, 80);
                cameraUpdated |= view.DrawToggle("視野角固定", timelineConfig.isFixedFoV, 80, ROW_HEIGHT, !currentLayer.isCameraLayer && studioHackManager.isPoseEditing, newValue =>
                {
                    timelineConfig.isFixedFoV = newValue;
                    timelineConfig.dirty = true;
                });

                WrapIfNeeded(view, 100);
                cameraUpdated |= view.DrawToggle("フォーカス固定", timelineConfig.isFixedFocus, 100, ROW_HEIGHT, !currentLayer.isCameraLayer && studioHackManager.isPoseEditing, newValue =>
                {
                    timelineConfig.isFixedFocus = newValue;
                    timelineConfig.dirty = true;
                });

                if (cameraUpdated)
                {
                    var cameraLayer = timelineManager.GetLayer(typeof(MTEP.CameraTimelineLayer));
                    if (cameraLayer != null)
                    {
                        cameraLayer.ApplyCurrentFrame(false);
                    }
                }
            }

            if (timelineManager.hasPostEffectLayer)
            {
                WrapIfNeeded(view, 100);
                view.DrawToggle("ポスプロ同期", timelineConfig.isPostEffectSync, 100, ROW_HEIGHT, !currentLayer.isPostEffectLayer, newValue =>
                {
                    timelineConfig.isPostEffectSync = newValue;
                    timelineConfig.dirty = true;
                });
            }
        }
    }
}
