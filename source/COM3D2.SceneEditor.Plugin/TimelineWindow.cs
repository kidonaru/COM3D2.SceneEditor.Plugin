using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインウィンドウ。MTE の MainWindow を SceneEditor のウィンドウ流儀に移植したもの。
    /// 上段に操作パネル、下段左にボーンメニュー、下段右にキーフレームグリッドを描く
    /// </summary>
    public class TimelineWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903383;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン";
        protected override int minWidth => 640;
        protected override int minHeight => 400;

        /// <summary>操作パネル部の高さ (この下がタイムライングリッド)</summary>
        private static readonly int CONTROL_HEIGHT = 190;
        private static readonly int MIN_MENU_WIDTH = 100;
        private static readonly int MAX_MENU_WIDTH = 300;
        /// <summary>フレーム番号バーの高さ</summary>
        private static readonly int FRAME_LABEL_HEIGHT = 20;

        private static TimelineWindow _instance = null;
        public static TimelineWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineWindow();
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
        private static MTEP.BoneMenuManager boneMenuManager => MTEP.BoneMenuManager.Instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static string anmName
        {
            get => timeline.anmName;
            set => timeline.anmName = value;
        }

        private static Color timelineLabelBgColor
        {
            get
            {
                var color = timelineConfig.timelineMenuBgColor;
                color.a = timelineConfig.timelineBgAlpha;
                return color;
            }
        }

        private readonly GUIView contentView = new GUIView();
        private readonly GUIView timelineView = new GUIView();
        private readonly GUIView boneMenuView = new GUIView();

        private bool initializedGUI = false;
        private bool requestUpdateTexture = false;
        private readonly GUIView.DragInfo frameDragInfo = new GUIView.DragInfo();
        private MTEP.BoneData frameDragBoneData = null;
        private readonly GUIView.DragInfo areaDragInfo = new GUIView.DragInfo();
        private Rect areaDragRect = new Rect();
        private readonly GUIView.DragInfo _menuWidthDraggableInfo = new GUIView.DragInfo();
        private int selectStartFrameNo = 0;
        private int selectEndFrameNo = 0;
        private bool isMultiSelect = false;

        private Texture2D texWhite => GUIView.texWhite;
        private Texture2D texTimelineBG = null;
        private Texture2D texKeyFrame = null;

        private readonly GUIStyle gsFrameLabel = new GUIStyle("label")
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };

        // 描画中の実効サイズ (contentRect から毎フレーム決定)
        private int _contentWidth = 640;
        private int _contentHeight = 480;

        private int timelineViewHeight => _contentHeight - CONTROL_HEIGHT - FRAME_LABEL_HEIGHT;

        private TimelineWindow()
        {
            MTEP.TimelineManager.onRefresh += () => requestUpdateTexture = true;
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelinePosX;
            y = config.timelinePosY;
            width = config.timelineWidth;
            height = config.timelineHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelinePosX = x;
            config.timelinePosY = y;
            config.timelineWidth = width;
            config.timelineHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineVisible;
            set => config.timelineVisible = value;
        }

        protected override void OnResizeEnd()
        {
            requestUpdateTexture = true;
        }

        public override void Update()
        {
            base.Update();

            if (!isWndVisible)
            {
                return;
            }

            if (requestUpdateTexture && !Input.GetMouseButton(0))
            {
                requestUpdateTexture = false;
                UpdateTexture();
            }

            UpdateKeyInput();
        }

        /// <summary>
        /// タイムライン操作のキーバインド (MTE 本体 Update から移植)。
        /// テキスト入力中は誤発動を防ぐため無視する
        /// </summary>
        private void UpdateKeyInput()
        {
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (studioHack == null || maidManager.maid == null ||
                !timelineManager.IsValidData())
            {
                return;
            }

            var tc = timelineConfig;

            if (tc.GetKeyDown(MTEP.KeyBindType.AddKeyFrame))
            {
                currentLayer.AddKeyFrameDiff();
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.AddKeyFrameAll))
            {
                currentLayer.AddKeyFrameAll();
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.RemoveKeyFrame))
            {
                timelineManager.RemoveSelectedFrame();
            }
            if (tc.GetKeyDownRepeat(MTEP.KeyBindType.PrevFrame))
            {
                timelineManager.SeekCurrentFrame(timelineManager.currentFrameNo - 1);
                FixScrollPosition();
            }
            if (tc.GetKeyDownRepeat(MTEP.KeyBindType.NextFrame))
            {
                timelineManager.SeekCurrentFrame(timelineManager.currentFrameNo + 1);
                FixScrollPosition();
            }
            if (tc.GetKeyDownRepeat(MTEP.KeyBindType.PrevKeyFrame))
            {
                var prevFrame = timelineManager.GetPrevFrame(timelineManager.currentFrameNo);
                if (prevFrame != null)
                {
                    timelineManager.SeekCurrentFrame(prevFrame.frameNo);
                    FixScrollPosition();
                }
            }
            if (tc.GetKeyDownRepeat(MTEP.KeyBindType.NextKeyFrame))
            {
                var nextFrame = timelineManager.GetNextFrame(timelineManager.currentFrameNo);
                if (nextFrame != null)
                {
                    timelineManager.SeekCurrentFrame(nextFrame.frameNo);
                    FixScrollPosition();
                }
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.Play))
            {
                if (currentLayer.isAnmPlaying)
                {
                    timelineManager.Pause();
                }
                else
                {
                    timelineManager.Play();
                }
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.EditMode))
            {
                studioHackManager.isPoseEditing = !studioHackManager.isPoseEditing;
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.Copy))
            {
                timelineManager.CopyFramesToClipboard();
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.Paste))
            {
                timelineManager.PasteFramesFromClipboard(false);
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.FlipPaste))
            {
                timelineManager.PasteFramesFromClipboard(true);
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.PoseCopy))
            {
                timelineManager.CopyPoseToClipboard();
            }
            if (tc.GetKeyDown(MTEP.KeyBindType.PosePaste))
            {
                timelineManager.PastePoseFromClipboard();
            }

            isMultiSelect = tc.GetKey(MTEP.KeyBindType.MultiSelect);
        }

        private void UpdateTexture()
        {
            if (timeline == null)
            {
                return;
            }

            MTEUtils.LogDebug("タイムラインテクスチャ作成中...");
            if (texTimelineBG != null)
            {
                UnityEngine.Object.Destroy(texTimelineBG);
                texTimelineBG = null;
            }

            var tc = timelineConfig;
            var bgWidth = _contentWidth - tc.menuWidth + tc.frameWidth * tc.frameNoInterval;
            bgWidth = Mathf.Min(bgWidth, tc.frameWidth * timeline.maxFrameCount);

            texTimelineBG = timeline.CreateBGTexture(
                tc.frameWidth,
                tc.frameHeight,
                bgWidth,
                timelineViewHeight + tc.frameHeight * 2,
                tc.timelineBgColor1,
                tc.timelineBgColor2,
                tc.timelineLineColor1,
                tc.timelineLineColor2,
                tc.frameNoInterval);

            if (texKeyFrame == null)
            {
                texKeyFrame = TextureUtils.CreateDiamondTexture(
                    tc.frameWidth,
                    Color.white);
            }
        }

        private void FixScrollPosition()
        {
            var viewWidth = _contentWidth - timelineConfig.menuWidth;
            var frameWidth = timelineConfig.frameWidth;

            var minScrollX = timelineManager.currentFrameNo * frameWidth - (viewWidth - 20 - frameWidth);
            var maxScrollX = timelineManager.currentFrameNo * frameWidth;
            timelineView.scrollPosition.x = Mathf.Clamp(timelineView.scrollPosition.x, minScrollX, maxScrollX);
        }

        /// <summary>ボーンメニューのスクロールバー非表示用スタイルを GUI.skin に登録する</summary>
        private void InitGUI()
        {
            if (initializedGUI)
            {
                return;
            }
            initializedGUI = true;

            var customStyles = new List<GUIStyle>(GUI.skin.customStyles);

            var names = new string[] {
                "invisible",
                "invisiblethumb",
                "invisibleleftbutton",
                "invisiblerightbutton",
                "invisibleupbutton",
                "invisibledownbutton",
            };

            foreach (var name in names)
            {
                var style = new GUIStyle
                {
                    name = name,
                    fixedWidth = 0,
                    fixedHeight = 0
                };
                style.normal.background = null;
                style.hover.background = null;
                style.active.background = null;
                customStyles.Add(style);
            }

            GUI.skin.customStyles = customStyles.ToArray();
        }

        protected override void DrawContent()
        {
            InitGUI();

            var local = ToLocalRect(contentRect);
            _contentWidth = (int)local.width;
            _contentHeight = (int)local.height;

            if (studioHack == null)
            {
                contentView.Init(local);
                contentView.DrawLabel("シーンが有効ではありません", -1, 20, Color.yellow);
                return;
            }

            var isStudioHackValid = studioHack.IsValid();
            var isMaidValid = maidManager.IsValid();

            bool editEnabled = isMaidValid
                            && isStudioHackValid
                            && timeline != null
                            && maidManager.maid != null;

            bool guiEnabled = contentView.focusedComboBox == null;

            DrawControlPanel(local, editEnabled, guiEnabled, isStudioHackValid, isMaidValid);

            if (texTimelineBG == null && editEnabled)
            {
                UpdateTexture();
            }

            DrawTimeline(local, editEnabled, guiEnabled);
            DrawBoneMenu(local, editEnabled, guiEnabled);
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

        /// <summary>ロード用のタイムラインファイル一覧 ("ディレクトリ/名前" 表記)</summary>
        private readonly GUIComboBox<string> _loadComboBox = new GUIComboBox<string>
        {
            defaultName = "ロード",
            getName = (path, index) => path,
            showArrow = false,
            buttonSize = new Vector2(60, 20),
            contentSize = new Vector2(250, 300),
        };

        private void RefreshTimelineFileList()
        {
            var items = new List<string>();
            try
            {
                var dirPath = MTEP.PluginUtils.TimelineDirPath;
                foreach (var path in Directory.GetFiles(dirPath, "*.xml", SearchOption.AllDirectories))
                {
                    var relative = path.Substring(dirPath.Length + 1)
                        .Replace('\\', '/');
                    items.Add(relative.Substring(0, relative.Length - ".xml".Length));
                }
                items.Sort();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            _loadComboBox.items = items;
        }

        private void LoadTimelineByRelativePath(string relative)
        {
            var slash = relative.LastIndexOf('/');
            var directoryName = slash >= 0 ? relative.Substring(0, slash).Replace('/', '\\') : "";
            var name = slash >= 0 ? relative.Substring(slash + 1) : relative;
            timelineManager.LoadTimeline(name, directoryName);
        }

        private readonly GUIComboBox<MTEP.TimelineLayerInfo> _layerComboBox = new GUIComboBox<MTEP.TimelineLayerInfo>
        {
            getName = (layerInfo, index) => layerInfo.displayName,
            onSelected = (layerInfo, index) =>
            {
                timelineManager.ChangeActiveLayer(layerInfo.layerType, maidManager.maidSlotNo);
            },
            contentSize = new Vector2(150, 300),
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

        private void DrawControlPanel(
            Rect local, bool editEnabled, bool guiEnabled,
            bool isStudioHackValid, bool isMaidValid)
        {
            var view = contentView;
            view.Init(local.x, local.y, local.width, CONTROL_HEIGHT);
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
                }

                _loadComboBox.currentIndex = -1;
                _loadComboBox.onSelected = (path, index) => LoadTimelineByRelativePath(path);
                if (_loadComboBox.items.Count == 0)
                {
                    RefreshTimelineFileList();
                }
                _loadComboBox.DrawButton(view);

                if (view.DrawButton("更新", 40, 20))
                {
                    RefreshTimelineFileList();
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
                    FixScrollPosition();
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

                view.DrawToggle("メイド表示", maidManager.maid.Visible, 80, 20, newValue =>
                {
                    maidManager.maid.Visible = newValue;
                });

                view.DrawToggle("背景表示", timeline.isBackgroundVisible, 80, 20, newValue =>
                {
                    timeline.isBackgroundVisible = newValue;
                });
            }
            view.EndLayout();
        }

        private void DrawTimeline(Rect local, bool editEnabled, bool guiEnabled)
        {
            if (!editEnabled || texTimelineBG == null)
            {
                return;
            }

            var view = timelineView;
            view.Init(local.x, local.y + CONTROL_HEIGHT, local.width, local.height - CONTROL_HEIGHT);
            view.SetEnabled(guiEnabled);

            view.BeginLayout(GUIView.LayoutDirection.Free);
            view.padding = Vector2.zero;

            var tc = timelineConfig;
            var menuWidth = tc.menuWidth;

            view.currentPos.x = menuWidth;
            view.currentPos.y = FRAME_LABEL_HEIGHT;

            var frameWidth = tc.frameWidth;
            var frameHeight = tc.frameHeight;
            var halfFrameWidth = frameWidth * 0.5f;

            var menuItems = boneMenuManager.GetVisibleItems();

            var contentWidth = timeline.maxFrameCount * frameWidth;
            var contentHeight = menuItems.Count * frameHeight;
            var viewWidth = _contentWidth - menuWidth;
            var viewHeight = timelineViewHeight;
            var scrollContentRect = new Rect(0, 0, contentWidth, contentHeight);
            bool alwaysShowHorizontal = true;
            bool alwaysShowVertical = !tc.isEasyEdit;

            // 自動スクロール
            if (tc.isAutoScroll &&
                currentLayer.isAnmSyncing &&
                studioHack.isAnmPlaying &&
                !(view.IsMouseOverRect(viewWidth, viewHeight) && Input.GetMouseButton(0)))
            {
                timelineView.scrollPosition.x = Mathf.Clamp(
                    timelineManager.currentFrameNo * frameWidth - viewWidth / 2,
                    0,
                    Mathf.Max(0, contentWidth - viewWidth));
            }

            view.BeginScrollView(
                viewWidth,
                viewHeight,
                scrollContentRect,
                alwaysShowHorizontal,
                alwaysShowVertical);

            var scrollPosition = view.scrollPosition;

            // 背景表示
            view.currentPos = Vector2.zero;
            var bgColor = Color.white;
            bgColor.a = tc.timelineBgAlpha;

            for (var i = 0; i < menuItems.Count; i += 2)
            {
                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y - frameHeight * 2)
                {
                    continue;
                }

                for (var j = 0; j < timeline.maxFrameCount; j += tc.frameNoInterval)
                {
                    view.currentPos.x = j * frameWidth;
                    if (view.currentPos.x < scrollPosition.x - frameWidth * tc.frameNoInterval)
                    {
                        continue;
                    }

                    view.DrawTexture(texTimelineBG, bgColor);
                    break;
                }

                break;
            }

            // 範囲選択表示
            if (selectStartFrameNo > 0 || selectEndFrameNo > 0)
            {
                var length = selectEndFrameNo - selectStartFrameNo + 1;
                view.currentPos.x = selectStartFrameNo * frameWidth;
                view.currentPos.y = scrollPosition.y;
                view.DrawTexture(texWhite, length * frameWidth, viewHeight, tc.timelineSelectRangeColor);
            }

            // 選択中のメニュー背景表示
            view.currentPos.x = scrollPosition.x;

            for (var i = 0; i < menuItems.Count; i++)
            {
                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var menuItem = menuItems[i];
                if (menuItem.isSelectedMenu)
                {
                    view.DrawTexture(
                        texWhite,
                        viewWidth,
                        frameHeight,
                        tc.timelineMenuSelectBgColor);
                }
            }

            // BPMライン表示
            if (timeline.isShowBPMLine && timeline.bpm > 0)
            {
                var frameNoPerBeat = timeline.frameRate * 60.0 / timeline.bpm;
                var offsetFrame = timeline.bpmLineOffsetFrame;
                var beatCount = timeline.maxFrameCount / frameNoPerBeat;
                for (var i = 1; i < beatCount; i++)
                {
                    var frameNo = Mathf.Round((float)(i * frameNoPerBeat) + offsetFrame);
                    view.currentPos.x = frameNo * frameWidth + halfFrameWidth;
                    if (view.currentPos.x < scrollPosition.x ||
                        view.currentPos.x > scrollPosition.x + viewWidth)
                    {
                        continue;
                    }

                    view.currentPos.y = 0;
                    view.DrawTexture(texWhite, 2, -1, tc.bpmLineColor);
                }
            }

            // トラック範囲表示
            var activeTrack = timeline.activeTrack;
            if (activeTrack != null)
            {
                view.currentPos.x = activeTrack.startFrameNo * frameWidth + halfFrameWidth;
                view.currentPos.y = 0;
                view.DrawTexture(texWhite, 2, -1, Color.red);

                view.currentPos.x = activeTrack.endFrameNo * frameWidth + halfFrameWidth;
                view.currentPos.y = 0;
                view.DrawTexture(texWhite, 2, -1, Color.red);
            }

            // 現在のフレーム表示
            view.currentPos.x = timelineManager.currentFrameNo * frameWidth + halfFrameWidth;
            view.currentPos.y = 0;
            view.DrawTexture(texWhite, 2, -1, Color.green);

            // キーフレーム表示
            var frames = currentLayer.keyFrames;
            var adjustY = (frameHeight - frameWidth) / 2;
            foreach (var frame in frames)
            {
                var frameNo = frame.frameNo;

                view.currentPos.x = frameNo * frameWidth;
                if (view.currentPos.x < scrollPosition.x ||
                    view.currentPos.x > scrollPosition.x + viewWidth)
                {
                    continue;
                }

                for (var i = 0; i < menuItems.Count; i++)
                {
                    var menuItem = menuItems[i];

                    view.currentPos.y = i * frameHeight + adjustY;
                    if (view.currentPos.y < scrollPosition.y ||
                        view.currentPos.y > scrollPosition.y + viewHeight - 20)
                    {
                        continue;
                    }

                    if (!menuItem.HasVisibleBone(frame))
                    {
                        continue;
                    }

                    bool isSelected = menuItem.IsSelectedFrame(frame);

                    var keyFrameRect = new Rect(
                            view.currentPos.x,
                            view.currentPos.y,
                            frameWidth,
                            frameWidth);

                    // エリア選択範囲内のキーフレームを選択
                    if (areaDragInfo.isDragging)
                    {
                        if (areaDragRect.Overlaps(keyFrameRect))
                        {
                            if (!isSelected)
                            {
                                menuItem.SelectFrame(frame, true);
                            }
                        }
                        else
                        {
                            if (isSelected && !isMultiSelect)
                            {
                                menuItem.SelectFrame(frame, true);
                            }
                        }
                    }

                    // フレームのドラッグ開始
                    if (!areaDragInfo.isDragging && !frameDragInfo.isDragging)
                    {
                        view.InvokeActionOnDragStart(
                            keyFrameRect,
                            frameDragInfo,
                            view.currentPos,
                            newPos =>
                            {
                                menuItem.SelectFrame(frame, isMultiSelect);
                                frameDragBoneData = timelineManager.selectedBones
                                    .Where(bone => bone.frameNo == frameNo)
                                    .FirstOrDefault();
                            }
                        );
                    }

                    var keyFrameColor = isSelected ? Color.red : Color.white;

                    if (!menuItem.IsFullBones(frame))
                    {
                        keyFrameColor *= Color.gray;
                    }

                    view.DrawTexture(
                        texKeyFrame,
                        frameWidth,
                        frameWidth,
                        keyFrameColor);
                }
            }

            // フレームのドラッグ中処理
            if (frameDragInfo.isDragging)
            {
                view.InvokeActionOnDragging(
                    frameDragInfo,
                    newPos =>
                    {
                        newPos.x = Mathf.Clamp(newPos.x, scrollPosition.x, scrollPosition.x + viewWidth - 20);
                        newPos.y = Mathf.Clamp(newPos.y, scrollPosition.y, scrollPosition.y + viewHeight - 20);

                        if (frameDragBoneData != null)
                        {
                            var targetFrameNo = (int)((newPos.x + halfFrameWidth) / frameWidth);
                            timelineManager.MoveSelectedBones(targetFrameNo - frameDragBoneData.frameNo);
                        }
                    });
            }

            view.currentPos = scrollPosition;

            // エリア選択のドラッグ開始
            if (!areaDragInfo.isDragging && !frameDragInfo.isDragging)
            {
                var drawRect = view.GetDrawRect(viewWidth - 20, viewHeight - 20);

                var pos = Event.current.mousePosition;
                pos.x -= drawRect.x;
                pos.y -= drawRect.y;
                pos += scrollPosition;

                view.InvokeActionOnDragStart(
                    drawRect,
                    areaDragInfo,
                    pos,
                    newPos =>
                    {
                        areaDragRect = new Rect(
                            areaDragInfo.startPos.x,
                            areaDragInfo.startPos.y,
                            0,
                            0);
                        if (!isMultiSelect)
                        {
                            timelineManager.UnselectAll();
                        }
                    }
                );
            }

            // エリア選択のドラッグ中処理
            if (areaDragInfo.isDragging)
            {
                view.InvokeActionOnDragging(
                    areaDragInfo,
                    newPos =>
                    {
                        newPos.x = Mathf.Clamp(newPos.x, scrollPosition.x, scrollPosition.x + viewWidth - 20);
                        newPos.y = Mathf.Clamp(newPos.y, scrollPosition.y, scrollPosition.y + viewHeight - 20);

                        areaDragRect.position = areaDragInfo.startPos;
                        areaDragRect.size = newPos - areaDragRect.position;

                        // エリア選択の座標を正規化
                        if (areaDragRect.width < 0)
                        {
                            areaDragRect.x += areaDragRect.width;
                            areaDragRect.width = -areaDragRect.width;
                        }
                        if (areaDragRect.height < 0)
                        {
                            areaDragRect.y += areaDragRect.height;
                            areaDragRect.height = -areaDragRect.height;
                        }
                    }
                );

                view.currentPos = areaDragRect.position;
                view.DrawRect(
                    areaDragRect.width,
                    areaDragRect.height,
                    new Color(1, 1, 1, 0.5f),
                    2);
            }

            view.EndScrollView();

            // 時間背景の表示
            view.currentPos.x = menuWidth;
            view.currentPos.y = 0;
            view.DrawTexture(texWhite, -1, FRAME_LABEL_HEIGHT, timelineLabelBgColor);

            // フレーム移動
            view.InvokeActionOnEvent(
                -1,
                FRAME_LABEL_HEIGHT,
                EventType.MouseDown,
                (pos) =>
                {
                    var frameNo = (int)((scrollPosition.x + pos.x) / frameWidth);
                    timelineManager.SeekCurrentFrame(frameNo);
                });

            // フレーム番号表示
            var frameLabelWidth = 50;
            var halfFrameLabelWidth = frameLabelWidth / 2;
            var adjustX = -halfFrameLabelWidth + halfFrameWidth;
            for (int frameNo = 0; frameNo < timeline.maxFrameCount; frameNo++)
            {
                view.currentPos.x = menuWidth + frameNo * frameWidth - scrollPosition.x + adjustX;
                if (view.currentPos.x < menuWidth - halfFrameLabelWidth ||
                    view.currentPos.x > _contentWidth - halfFrameLabelWidth)
                {
                    continue;
                }

                if (frameNo == timelineManager.currentFrameNo)
                {
                    view.DrawLabel(frameNo.ToString(), frameLabelWidth, 20, Color.green, gsFrameLabel);
                }
                else if (frameNo % tc.frameNoInterval == 0)
                {
                    view.DrawLabel(frameNo.ToString(), frameLabelWidth, 20, Color.white, gsFrameLabel);
                }
            }

            view.EndLayout();
        }

        private void DrawBoneMenu(Rect local, bool editEnabled, bool guiEnabled)
        {
            if (!editEnabled)
            {
                return;
            }

            var view = boneMenuView;
            var tc = timelineConfig;
            var menuWidth = tc.menuWidth;

            view.Init(local.x, local.y + CONTROL_HEIGHT, menuWidth, local.height - CONTROL_HEIGHT);
            view.SetEnabled(guiEnabled);

            view.BeginLayout(GUIView.LayoutDirection.Free);
            view.padding = Vector2.zero;

            var frameHeight = tc.frameHeight;

            var menuItems = boneMenuManager.GetVisibleItems();

            // ボーンメニューの表示
            view.currentPos.x = 0;
            view.currentPos.y = FRAME_LABEL_HEIGHT;
            view.DrawTexture(texWhite, menuWidth, -1, timelineLabelBgColor);

            view.scrollPosition.y = timelineView.scrollPosition.y;
            var contentWidth = menuWidth;
            var contentHeight = menuItems.Count * frameHeight;
            var viewWidth = menuWidth;
            var viewHeight = timelineViewHeight - 20;
            var scrollContentRect = new Rect(0, 0, contentWidth, contentHeight);
            view.BeginScrollView(
                viewWidth,
                viewHeight,
                scrollContentRect,
                "invisible",
                "invisible");

            var scrollPosition = view.scrollPosition;
            timelineView.scrollPosition.y = scrollPosition.y;

            for (int i = 0; i < menuItems.Count; i++)
            {
                var menuItem = menuItems[i];

                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var diplayName = menuItem.displayName;
                var isSelected = menuItem.isSelectedMenu;

                view.currentPos.x = 0;

                if (menuItem.isSetMenu)
                {
                    view.DrawLabel(
                        menuItem.isOpenMenu ? "ー" : "＋",
                        20,
                        20,
                        isSelected ? tc.timelineMenuSelectTextColor : Color.white,
                        null,
                        () =>
                        {
                            menuItem.isOpenMenu = !menuItem.isOpenMenu;
                        }
                    );
                }

                view.currentPos.x = 20;

                view.DrawLabel(
                    diplayName,
                    menuWidth - 20,
                    20,
                    isSelected ? tc.timelineMenuSelectTextColor : Color.white
                );

                view.InvokeActionOnEvent(
                    menuWidth - 40,
                    20,
                    EventType.MouseDown,
                    (pos) =>
                    {
                        menuItem.SelectMenu(isMultiSelect);
                    });

                if (studioHackManager.isPoseEditing)
                {
                    view.InvokeActionOnMouse(
                        menuWidth - 20,
                        20,
                        _ =>
                        {
                            view.currentPos.x = menuWidth - 20;

                            var frame = currentLayer.GetFrame(timelineManager.currentFrameNo);
                            if (menuItem.IsFullBones(frame))
                            {
                                if (view.DrawButton("D", 20, 20))
                                {
                                    menuItem.RemoveKey();
                                }
                            }
                            else
                            {
                                if (view.DrawButton("A", 20, 20))
                                {
                                    menuItem.AddKey();
                                }
                            }
                        });
                }
            }
            view.EndScrollView();

            // メニュー幅の変更ボタン
            view.currentPos.x = view.viewRect.width - 20;
            view.currentPos.y = view.viewRect.height - 20;

            var buttonRect = view.GetDrawRect(20, 20);
            if (buttonRect.Contains(Event.current.mousePosition) ||
                _menuWidthDraggableInfo.isDragging)
            {
                view.DrawDraggableButton("□", 20, 20,
                    _menuWidthDraggableInfo,
                    new Vector2(tc.menuWidth, 0f),
                    null,
                    value =>
                {
                    tc.menuWidth = (int)value.x;
                    tc.menuWidth = Mathf.Clamp(tc.menuWidth, MIN_MENU_WIDTH, MAX_MENU_WIDTH);

                    requestUpdateTexture = true;
                    tc.dirty = true;
                });
            }

            view.EndLayout();
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                RefreshTimelineFileList();
                requestUpdateTexture = true;
            }
        }
    }
}
