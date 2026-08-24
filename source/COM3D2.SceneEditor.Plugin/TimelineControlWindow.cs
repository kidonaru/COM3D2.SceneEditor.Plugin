using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作ウィンドウ。TimelineWindow 上段にあった操作パネル
    /// (ファイル/フレーム操作/キーフレーム/範囲操作/レイヤー/表示トグル) を独立させたもの
    /// </summary>
    public class TimelineControlWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903391;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン操作";
        protected override int minWidth => 640;
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

        private readonly GUIComboBox<MTEP.TimelineLayerInfo> _layerComboBox = new GUIComboBox<MTEP.TimelineLayerInfo>
        {
            getName = (layerInfo, index) => layerInfo.displayName,
            onSelected = (layerInfo, index) =>
            {
                timelineManager.ChangeActiveLayer(layerInfo.layerType, maidManager.maidSlotNo);
            },
            contentSize = new Vector2(150, 300),
        };

        /// <summary>未使用レイヤーの追加コンボ。選択と同時にアクティブ化する</summary>
        private readonly GUIComboBox<MTEP.TimelineLayerInfo> _addLayerComboBox = new GUIComboBox<MTEP.TimelineLayerInfo>
        {
            getName = (layerInfo, index) => layerInfo.displayName,
            onSelected = (layerInfo, index) =>
            {
                timelineManager.ChangeActiveLayer(layerInfo.layerType, maidManager.maidSlotNo);
                // MTE では追加後にレイヤー情報サブウィンドウを開くため、対応する編集ウィンドウを開く
                if (!TimelineLayerWindow.instance.isShowWnd)
                {
                    WindowManager.ToggleWindowVisible(TimelineLayerWindow.instance);
                }
            },
            defaultName = "+",
            buttonSize = new Vector2(20, 20),
            contentSize = new Vector2(150, 300),
            showArrow = false,
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

        protected override void DrawContent()
        {
            DrawBody();

            // 早期 return してもコンボのポップアップ処理を飛ばさないよう本体と分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            var local = ToLocalRect(contentRect);

            if (studioHack == null)
            {
                _view.Init(local);
                _view.DrawLabel("シーンが有効ではありません", -1, 20, Color.yellow);
                return;
            }

            var isStudioHackValid = studioHack.IsValid();
            var isMaidValid = maidManager.IsValid();

            bool editEnabled = isMaidValid
                            && isStudioHackValid
                            && timeline != null
                            && maidManager.maid != null;

            bool guiEnabled = _view.focusedComboBox == null;

            var view = _view;
            view.Init(local);
            view.SetEnabled(guiEnabled);

            view.margin = 0;
            view.padding = new Vector2(3, 3);

            view.BeginHorizontal();
            {
                fileMenuComboBox.currentIndex = -1;
                fileMenuComboBox.DrawButton(view);

                if (view.DrawButton("セーブ", 60, 20, editEnabled))
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

                // MTE のロードボタンと同様に開く動作のみ (他ボタンと違いトグルしない)
                if (view.DrawButton("ロード", 60, 20))
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

                // MTE のトラックボタンと同様、アクティブトラックありを緑で示す (SE はトラック UI が設定ウィンドウ内)
                var trackColor = editEnabled && timeline.activeTrack != null ? Color.green : Color.white;
                if (view.DrawButton("設定", 50, 20, true, trackColor))
                {
                    WindowManager.ToggleWindowVisible(TimelineSettingWindow.instance);
                }

                if (view.DrawButton("編集", 50, 20))
                {
                    WindowManager.ToggleWindowVisible(TimelineLayerWindow.instance);
                }

                if (view.DrawButton("テンプレ", 60, 20))
                {
                    WindowManager.ToggleWindowVisible(TimelineTemplateWindow.instance);
                }

                view.AddSpace(20);

                // 状態メッセージ
                if (!isStudioHackValid)
                {
                    view.DrawLabel(studioHack.errorMessage, 400, 20, Color.yellow);
                }
                else if (!isMaidValid)
                {
                    view.DrawLabel(maidManager.errorMessage, 400, 20, Color.yellow);
                }
                else if (!timelineManager.IsValidData())
                {
                    view.DrawLabel(timelineManager.errorMessage, 400, 20, Color.yellow);
                }
                else if (studioHackManager.isPoseEditing)
                {
                    var keyName = timelineConfig.GetKeyName(MTEP.KeyBindType.AddKeyFrame);
                    view.DrawLabel("[" + keyName + "]キーでキーフレームを登録します", 400, 20, Color.white);
                }
                else
                {
                    var keyName = timelineConfig.GetKeyName(MTEP.KeyBindType.EditMode);
                    view.DrawLabel("[" + keyName + "]キーで編集モードに切り替えます", 400, 20, Color.white);
                }
            }
            view.EndLayout();

            view.margin = GUIView.defaultMargin;
            view.padding = GUIView.defaultPadding;

            if (!editEnabled)
            {
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawTextField("アニメ名", 0, anmName, 310, 20, newText => anmName = newText);

                view.AddSpace(10);

                view.DrawLabel("最終フレーム", 75, 20);

                var newMaxFrameNo = timeline.maxFrameNo;

                view.DrawIntSelect(
                    "",
                    1,
                    10,
                    null,
                    newMaxFrameNo,
                    value => newMaxFrameNo = value,
                    diff => newMaxFrameNo += diff
                );

                if (newMaxFrameNo != timeline.maxFrameNo)
                {
                    timelineManager.SetMaxFrameNo(newMaxFrameNo);
                }
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("フレーム操作", 100, 20);

                var newFrameNo = timelineManager.currentFrameNo;

                view.margin = 0;

                if (view.DrawButton("|<", 25, 20))
                {
                    newFrameNo = 0;
                }
                if (view.DrawRepeatButton(".<", 25, 20))
                {
                    var prevFrame = timelineManager.GetPrevFrame(newFrameNo);
                    if (prevFrame != null)
                    {
                        newFrameNo = prevFrame.frameNo;
                    }
                }
                if (view.DrawRepeatButton("<", 25, 20))
                {
                    newFrameNo--;
                }

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = newFrameNo,
                    width = 50,
                    height = 20,
                    onChanged = value => newFrameNo = value,
                });

                if (view.DrawRepeatButton(">", 25, 20))
                {
                    newFrameNo++;
                }
                if (view.DrawRepeatButton(">.", 25, 20))
                {
                    var nextFrame = timelineManager.GetNextFrame(newFrameNo);
                    if (nextFrame != null)
                    {
                        newFrameNo = nextFrame.frameNo;
                    }
                }
                if (view.DrawButton(">|", 25, 20))
                {
                    newFrameNo = timeline.maxFrameNo;
                }

                view.margin = GUIView.defaultMargin;

                if (newFrameNo != timelineManager.currentFrameNo)
                {
                    timelineManager.SeekCurrentFrame(newFrameNo);
                    TimelineWindow.instance.FixScrollPosition();
                }

                view.AddSpace(10);

                if (currentLayer.isAnmPlaying)
                {
                    if (view.DrawButton("■", 20, 20))
                    {
                        timelineManager.Pause();
                    }
                }
                else
                {
                    if (view.DrawButton("▶", 20, 20))
                    {
                        timelineManager.Play();
                    }
                }

                view.AddSpace(10);

                view.DrawSliderValue(
                    new GUIView.SliderOption
                    {
                        label = "再生速度",
                        labelWidth = 50,
                        min = 0f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 1f,
                        value = timelineManager.anmSpeed,
                        onChanged = value => timelineManager.anmSpeed = value,
                    });
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("キーフレーム", 100, 20);

                if (view.DrawButton("登録", 50, 20, studioHackManager.isPoseEditing))
                {
                    currentLayer.AddKeyFrameDiff();
                }

                if (view.DrawButton("全登録", 60, 20))
                {
                    currentLayer.AddKeyFrameAll();
                }

                if (view.DrawButton("削除", 50, 20, timelineManager.HasSelected()))
                {
                    timelineManager.RemoveSelectedFrame();
                }

                if (view.DrawButton("コピー", 60, 20, timelineManager.HasSelected()))
                {
                    timelineManager.CopyFramesToClipboard();
                }

                if (view.DrawButton("ペースト", 60, 20))
                {
                    timelineManager.PasteFramesFromClipboard(false);
                }

                if (view.DrawButton("反転P", 60, 20))
                {
                    timelineManager.PasteFramesFromClipboard(true);
                }

                if (view.DrawButton("ポーズC", 60, 20))
                {
                    timelineManager.CopyPoseToClipboard();
                }

                if (view.DrawButton("ポーズP", 60, 20, studioHackManager.isPoseEditing))
                {
                    timelineManager.PastePoseFromClipboard();
                }
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("範囲操作", 100, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = selectStartFrameNo,
                    width = 50,
                    height = 20,
                    onChanged = value => selectStartFrameNo = value,
                });

                view.DrawLabel("～", 15, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = selectEndFrameNo,
                    width = 50,
                    height = 20,
                    onChanged = value => selectEndFrameNo = value,
                });

                if (view.DrawButton("R", 20, 20))
                {
                    selectStartFrameNo = 0;
                    selectEndFrameNo = 0;
                }

                var isValidRange = timelineManager.IsValidFrameRnage(selectStartFrameNo, selectEndFrameNo);

                if (view.DrawButton("範囲選択", 65, 20))
                {
                    timelineManager.SelectFramesRange(selectStartFrameNo, selectEndFrameNo);
                }

                if (view.DrawButton("ﾌﾚｰﾑ挿入", 65, 20, isValidRange && selectStartFrameNo > 0))
                {
                    timelineManager.InsertFrames(selectStartFrameNo, selectEndFrameNo);
                }

                if (view.DrawButton("ﾌﾚｰﾑ削除", 65, 20, isValidRange && selectStartFrameNo > 0))
                {
                    timelineManager.DeleteFrames(selectStartFrameNo, selectEndFrameNo);
                }

                if (view.DrawButton("ﾌﾚｰﾑ複製", 65, 20, isValidRange))
                {
                    timelineManager.DuplicateFrames(selectStartFrameNo, selectEndFrameNo);
                }

                if (view.DrawButton("縦選択", 60, 20, !timelineConfig.isEasyEdit))
                {
                    timelineManager.SelectVerticalBones();
                }
            }
            view.EndLayout();

            view.BeginHorizontal();
            view.margin = 0;
            {
                var layerType = currentLayer.layerType;
                var layerInfo = timelineManager.GetLayerInfo(layerType);
                _layerComboBox.currentItem = layerInfo;
                _layerComboBox.items = timelineManager.usingLayerInfoList;
                _layerComboBox.DrawButton("レイヤー", view);

                if (view.DrawButton("-", 20, 20, layerType != typeof(MTEP.MotionTimelineLayer)))
                {
                    timelineManager.RemoveLayers(layerType);
                }

                _addLayerComboBox.currentIndex = -1;
                _addLayerComboBox.items = timelineManager.unusingLayerInfoList;
                _addLayerComboBox.DrawButton(null, view);

                view.AddSpace(10);

                if (currentLayer.hasSlotNo)
                {
                    view.DrawLabel("操作対象", 60, 20);

                    _maidComboBox.currentIndex = currentLayer.slotNo;
                    _maidComboBox.items = maidManager.maidCaches;
                    _maidComboBox.DrawButton(view);
                }
            }
            view.margin = GUIView.defaultMargin;
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("簡易表示", timelineConfig.isEasyEdit, 80, 20, newValue =>
                {
                    timelineConfig.isEasyEdit = newValue;
                    timelineConfig.dirty = true;
                    timelineManager.Refresh();
                });

                view.DrawToggle("編集モード", studioHackManager.isPoseEditing, 80, 20, newValue =>
                {
                    studioHackManager.isPoseEditing = newValue;
                });

                view.DrawToggle("自動登録", timelineConfig.isAutoKeyFrame, 80, 20, newValue =>
                {
                    timelineConfig.isAutoKeyFrame = newValue;
                    timelineConfig.dirty = true;
                });

                view.DrawToggle("メイド表示", maidManager.maid.Visible, 80, 20, newValue =>
                {
                    maidManager.maid.Visible = newValue;
                });

                view.DrawToggle("モデル表示", modelManager.Visible, 80, 20, newValue =>
                {
                    modelManager.Visible = newValue;
                });

                view.DrawToggle("背景表示", timeline.isBackgroundVisible, 80, 20, newValue =>
                {
                    timeline.isBackgroundVisible = newValue;
                });

                if (timelineManager.hasCameraLayer)
                {
                    var cameraUpdated = false;
                    cameraUpdated |= view.DrawToggle("カメラ同期", timelineConfig.isCameraSync, 80, 20, !currentLayer.isCameraLayer, newValue =>
                    {
                        timelineConfig.isCameraSync = newValue;
                        timelineConfig.dirty = true;
                    });

                    cameraUpdated |= view.DrawToggle("視野角固定", timelineConfig.isFixedFoV, 80, 20, !currentLayer.isCameraLayer && studioHackManager.isPoseEditing, newValue =>
                    {
                        timelineConfig.isFixedFoV = newValue;
                        timelineConfig.dirty = true;
                    });

                    cameraUpdated |= view.DrawToggle("フォーカス固定", timelineConfig.isFixedFocus, 100, 20, !currentLayer.isCameraLayer && studioHackManager.isPoseEditing, newValue =>
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
                    view.DrawToggle("ポスプロ同期", timelineConfig.isPostEffectSync, 100, 20, !currentLayer.isPostEffectLayer, newValue =>
                    {
                        timelineConfig.isPostEffectSync = newValue;
                        timelineConfig.dirty = true;
                    });
                }
            }
            view.EndLayout();
        }
    }
}
