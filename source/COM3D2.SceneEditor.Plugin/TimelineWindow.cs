using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインウィンドウ。MTE の MainWindow を SceneEditor のウィンドウ流儀に移植したもの。
    /// 左にボーンメニュー、右にキーフレームグリッドを描く。
    /// 操作パネルは TimelineControlWindow として独立している
    /// </summary>
    public class TimelineWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903383;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン";
        protected override int minWidth => 320;
        protected override int minHeight => 160;

        private static readonly int MIN_MENU_WIDTH = 100;
        private static readonly int MAX_MENU_WIDTH = 300;
        /// <summary>フレーム番号バーの高さ</summary>
        private static readonly int FRAME_LABEL_HEIGHT = 20;
        /// <summary>レイヤー行の削除ボタン / 追加コンボの幅</summary>
        private static readonly int LAYER_BUTTON_WIDTH = 20;

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

        private static Color timelineLabelBgColor
        {
            get
            {
                var color = timelineConfig.timelineMenuBgColor;
                color.a = timelineConfig.timelineBgAlpha;
                return color;
            }
        }

        /// <summary>コンボのフォーカス状態を各ビューで共有するためのルート</summary>
        private readonly GUIView _rootView = new GUIView();
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

        /// <summary>表示レイヤーの複数選択コンボ。ボーンメニュー上部に置く</summary>
        private readonly GUIMultiSelectComboBox<MTEP.ITimelineLayer> _displayLayerComboBox
            = new GUIMultiSelectComboBox<MTEP.ITimelineLayer>
        {
            contentSize = new Vector2(200, 300),
            // menuWidth (100〜300px) に収めるため前後送りの矢印は省略する
            showArrow = false,
        };

        /// <summary>表示レイヤー集合と折りたたみ集合 (セッション内のみ保持)</summary>
        private readonly TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem> _rowState
            = new TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>();

        /// <summary>今フレームの表示行 (カテゴリ行 + ボーンメニュー行)。DrawBody で再構築する</summary>
        private readonly List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>> _rows
            = new List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>>(256);

        /// <summary>タイムライン差し替え検知用。別インスタンスになったら表示状態をリセットする</summary>
        private MTEP.TimelineData _lastTimeline = null;

        /// <summary>レイヤー数の変化検知用。Prune を毎フレーム走らせないためのガード</summary>
        private int _lastLayerCount = -1;

        /// <summary>追加コンボ用の「現在のメイドで未使用の型」一覧バッファ (毎フレーム詰め直す)</summary>
        private readonly List<MTEP.TimelineLayerInfo> _addableLayerInfoList
            = new List<MTEP.TimelineLayerInfo>(32);

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
            buttonSize = new Vector2(LAYER_BUTTON_WIDTH, FRAME_LABEL_HEIGHT),
            contentSize = new Vector2(150, 300),
            showArrow = false,
        };

        private bool isMultiSelect = false;

        private Texture2D texWhite => GUIView.texWhite;
        private Texture2D texTimelineBG = null;
        private Texture2D texKeyFrame = null;

        private readonly GUIStyle gsFrameLabel = new GUIStyle("label")
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            // スキン既定の文字色が暗くシークバー上で読めないため白を明示する
            normal = { textColor = Color.white }
        };

        // 描画中の実効サイズ (contentRect から毎フレーム決定)
        private int _contentWidth = 640;
        private int _contentHeight = 480;

        /// <summary>カーブエディタが占める高さ (閉じていてもトグルバー分は常に確保する)</summary>
        private float curvePaneTotalHeight =>
            TimelineCurveEditor.instance.isOpen
                ? TimelineCurveEditor.instance.paneHeight + TimelineCurveEditor.TOGGLE_BAR_HEIGHT
                : TimelineCurveEditor.TOGGLE_BAR_HEIGHT;

        // ウィンドウが低い状態でペインを開いてもドープシートが潰れないようガードする
        private int timelineViewHeight => Mathf.Max(
            60, (int)(_contentHeight - FRAME_LABEL_HEIGHT - curvePaneTotalHeight));

        /// <summary>ドープシート/ボーンメニューの下端 = カーブエディタ領域の上端</summary>
        private int curvePaneTop => FRAME_LABEL_HEIGHT + timelineViewHeight;

        private TimelineWindow()
        {
            MTEP.TimelineManager.onRefresh += () => requestUpdateTexture = true;
            SelectionManager.instance.onSelectionChanged += OnSelectionChanged;
            MaidDragBoneTracker.onDragCompleted += OnDragCompleted;

            // フィールド初期化子ではインスタンスメンバーを参照できないためここで設定する
            _displayLayerComboBox.getName = (layer, _) => GetLayerDisplayName(layer);
            _displayLayerComboBox.getChecked = (layer, _) => _rowState.IsVisible(layer, currentLayer);
            _displayLayerComboBox.onToggle = (layer, _) => _rowState.ToggleVisible(layer, currentLayer);
        }

        // ドラッグ編集完了時の自動キーフレーム登録 (SE 独自機能、既定 OFF)
        private void OnDragCompleted(Maid maid)
        {
            if (!MTEP.ConfigManager.instance.config.isAutoKeyFrame)
            {
                return;
            }

            var timelineManager = MTEP.TimelineManager.instance;
            var currentLayer = timelineManager.currentLayer;
            // 編集モード外 (initialEditFrame 未設定) のドラッグはキーフレーム登録の対象外
            if (currentLayer == null || timelineManager.initialEditFrame == null)
            {
                return;
            }

            // 指ドラッグ等は選択同期を経ずレイヤーが別メイドを指したままになり得るため、
            // アクティブレイヤーの対象メイドと一致する場合のみ登録する
            var maidCache = currentLayer.maidCache;
            if (!currentLayer.hasSlotNo || maidCache == null || maidCache.maid != maid)
            {
                return;
            }

            currentLayer.AddKeyFrameDiff();
        }

        private bool _syncingSelection = false;

        // Hierarchy 等での選択をタイムラインのアクティブメイド/レイヤーへ同期する
        private void OnSelectionChanged(GameObject go)
        {
            if (_syncingSelection || go == null)
            {
                return;
            }

            var timelineManager = MTEP.TimelineManager.instance;
            var currentLayer = timelineManager.currentLayer;
            if (timelineManager.timeline == null || currentLayer == null)
            {
                return;
            }

            try
            {
                _syncingSelection = true;

                // メイド（配下ボーン含む）なら該当スロットのレイヤーへ切替
                var maid = go.GetComponentInParent<Maid>();
                if (maid != null)
                {
                    var maidCaches = MTEP.MaidManager.instance.maidCaches;
                    for (var i = 0; i < maidCaches.Count; i++)
                    {
                        if (maidCaches[i].maid == maid)
                        {
                            if (currentLayer.hasSlotNo && currentLayer.slotNo != i)
                            {
                                timelineManager.ChangeActiveLayer(currentLayer.layerType, i);
                            }
                            return;
                        }
                    }
                    return;
                }

                // 追加ライトならライトレイヤーへ切替 (LightTimelineLayer は slotNo を持たない単一レイヤー)
                var light = go.GetComponentInChildren<Light>();
                if (light != null && StudioLightManager.instance.lights.Contains(light))
                {
                    if (currentLayer.layerType != typeof(MTEP.LightTimelineLayer))
                    {
                        timelineManager.ChangeActiveLayer(typeof(MTEP.LightTimelineLayer), 0);
                    }
                }
            }
            finally
            {
                _syncingSelection = false;
            }
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

            // リサイズ確定 (OnResizeEnd) 由来の再構築はボタン離上イベントを伴わないため、
            // MTE の GetMouseButtonUp ではなく「非ドラッグ中」で判定する
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

        /// <summary>シーク後に現在フレームが見える位置へスクロールを寄せる。TimelineControlWindow からも呼ばれる</summary>
        public void FixScrollPosition()
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
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 各ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            contentView.parent = _rootView;
            timelineView.parent = _rootView;
            boneMenuView.parent = _rootView;

            DrawBody();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            // (BackgroundWindow と同じ流儀。これを呼ばないとフォーカスが残留し
            //  guiEnabled=false のまま全 UI が無効化されて操作不能になる)
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>
        /// 本体の描画。早期 return しても DrawContent 末尾の
        /// ProcessFocus を飛ばさないようメソッドを分けている
        /// </summary>
        private void DrawBody()
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

            bool editEnabled = maidManager.IsValid()
                            && studioHack.IsValid()
                            && timeline != null
                            && maidManager.maid != null;

            bool guiEnabled = contentView.focusedComboBox == null;

            // タイムラインが差し替わったら表示状態を初期化 (アクティブのみ表示・全展開)
            if (timeline != _lastTimeline)
            {
                _lastTimeline = timeline;
                _rowState.Reset();
            }

            if (editEnabled)
            {
                // レイヤーの追加・削除は必ず数の変化を伴うため、Prune は数が変わったときだけで足りる
                // (同一フレームでの入れ替えで数が同じ場合、残った死に参照は layers に無いので描画されず無害)
                if (timelineManager.layers.Count != _lastLayerCount)
                {
                    _lastLayerCount = timelineManager.layers.Count;
                    _rowState.Prune(timelineManager.layers);
                }
                BuildRows();
            }

            if (texTimelineBG == null && editEnabled)
            {
                UpdateTexture();
            }

            DrawTimeline(local, editEnabled, guiEnabled);
            DrawBoneMenu(local, editEnabled, guiEnabled);
        }

        /// <summary>表示行リストを組み立てる。簡易表示時は従来どおり単一レイヤーでカテゴリ行なし</summary>
        private void BuildRows()
        {
            if (timelineConfig.isEasyEdit)
            {
                _rows.Clear();
                foreach (var item in boneMenuManager.GetVisibleItems())
                {
                    _rows.Add(new LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>
                    {
                        layer = currentLayer,
                        menuItem = item,
                    });
                }
                return;
            }

            _rowState.BuildRows(timelineManager.layers, currentLayer, CollectVisibleItems, _rows);
        }

        private void CollectVisibleItems(MTEP.ITimelineLayer layer, List<MTEP.IBoneMenuItem> result)
        {
            boneMenuManager.GetVisibleItems(layer, result);
        }

        private void DrawTimeline(Rect local, bool editEnabled, bool guiEnabled)
        {
            if (!editEnabled || texTimelineBG == null)
            {
                return;
            }

            var view = timelineView;
            view.Init(local.x, local.y, local.width, local.height);
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

            var contentWidth = timeline.maxFrameCount * frameWidth;
            var contentHeight = _rows.Count * frameHeight;
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

            for (var i = 0; i < _rows.Count; i += 2)
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

            // 範囲選択表示 (範囲操作の入力値は操作ウィンドウ側が持つ)
            var selectStartFrameNo = TimelineControlWindow.instance.selectStartFrameNo;
            var selectEndFrameNo = TimelineControlWindow.instance.selectEndFrameNo;
            if (selectStartFrameNo > 0 || selectEndFrameNo > 0)
            {
                var length = selectEndFrameNo - selectStartFrameNo + 1;
                view.currentPos.x = selectStartFrameNo * frameWidth;
                view.currentPos.y = scrollPosition.y;
                view.DrawTexture(texWhite, length * frameWidth, viewHeight, tc.timelineSelectRangeColor);
            }

            // 選択中のメニュー背景表示
            view.currentPos.x = scrollPosition.x;

            for (var i = 0; i < _rows.Count; i++)
            {
                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var row = _rows[i];

                // カテゴリ行はドープシート側ではキーを持たない帯として塗る
                if (row.isHeader)
                {
                    view.DrawTexture(
                        texWhite,
                        viewWidth,
                        frameHeight,
                        timelineLabelBgColor);
                    continue;
                }

                // 選択ハイライトはアクティブレイヤーの行のみ
                if (row.layer == timelineManager.currentLayer && row.menuItem.isSelectedMenu)
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

            // キーフレーム表示。行リストを同一レイヤーの連続ブロックごとに走査する
            var adjustY = (frameHeight - frameWidth) / 2;
            var blockStart = 0;
            while (blockStart < _rows.Count)
            {
                var blockLayer = _rows[blockStart].layer;
                var blockEnd = blockStart;
                while (blockEnd < _rows.Count && _rows[blockEnd].layer == blockLayer)
                {
                    blockEnd++;
                }

                // 折りたたみ中 (カテゴリ行のみでアイテム行なし) は keyFrames 走査ごとスキップする
                if (blockEnd - blockStart == 1 && _rows[blockStart].isHeader)
                {
                    blockStart = blockEnd;
                    continue;
                }

                var isActiveLayer = blockLayer == timelineManager.currentLayer;

                foreach (var frame in blockLayer.keyFrames)
                {
                    var frameNo = frame.frameNo;

                    view.currentPos.x = frameNo * frameWidth;
                    if (view.currentPos.x < scrollPosition.x ||
                        view.currentPos.x > scrollPosition.x + viewWidth)
                    {
                        continue;
                    }

                    for (var i = blockStart; i < blockEnd; i++)
                    {
                        var row = _rows[i];
                        if (row.isHeader)
                        {
                            continue;
                        }

                        var menuItem = row.menuItem;

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

                        // 選択状態はアクティブレイヤーにしか存在しない
                        bool isSelected = isActiveLayer && menuItem.IsSelectedFrame(frame);

                        var keyFrameRect = new Rect(
                                view.currentPos.x,
                                view.currentPos.y,
                                frameWidth,
                                frameWidth);

                        // エリア選択範囲内のキーフレームを選択 (アクティブレイヤーのみ)
                        if (isActiveLayer && areaDragInfo.isDragging)
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

                        // フレームのドラッグ開始。非アクティブレイヤーはまずアクティブ化してから選択する
                        if (!areaDragInfo.isDragging && !frameDragInfo.isDragging)
                        {
                            view.InvokeActionOnDragStart(
                                keyFrameRect,
                                frameDragInfo,
                                view.currentPos,
                                newPos =>
                                {
                                    if (row.layer != timelineManager.currentLayer)
                                    {
                                        timelineManager.SetCurrentLayer(row.layer);
                                    }
                                    menuItem.SelectFrame(frame, isMultiSelect);
                                    frameDragBoneData = timelineManager.selectedBones
                                        .Where(bone => bone.frameNo == frameNo)
                                        .FirstOrDefault();

                                    // 消費しないと GUI.DragWindow が拾ってウィンドウごと動いてしまう
                                    Event.current.Use();
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

                blockStart = blockEnd;
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

                        // 消費しないと GUI.DragWindow が拾って矩形選択にならない
                        Event.current.Use();
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

            // カーブエディタペイン (ドープシート下部)
            var curveEditor = TimelineCurveEditor.instance;
            var paneTop = curvePaneTop;
            curveEditor.DrawToggleBar(
                view,
                new Rect(menuWidth, paneTop, viewWidth, TimelineCurveEditor.TOGGLE_BAR_HEIGHT));
            if (curveEditor.isOpen)
            {
                // ウィンドウ下端をはみ出さないようにペイン高さをクランプする
                var paneY = paneTop + TimelineCurveEditor.TOGGLE_BAR_HEIGHT;
                var paneHeight = Mathf.Min(curveEditor.paneHeight, _contentHeight - paneY);
                if (paneHeight > 0f)
                {
                    curveEditor.DrawPane(
                        view,
                        new Rect(menuWidth, paneY, viewWidth, paneHeight),
                        scrollPosition.x,
                        frameWidth,
                        guiEnabled);
                }
            }

            view.EndLayout();
        }

        /// <summary>
        /// レイヤーインスタンスの表示名。スロット付きレイヤーはメイド名を併記して
        /// 同型レイヤーのインスタンスを区別できるようにする
        /// </summary>
        private string GetLayerDisplayName(MTEP.ITimelineLayer layer)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            var name = info != null ? info.displayName : layer.layerName;
            if (layer.hasSlotNo)
            {
                var maidCache = layer.maidCache;
                var maidName = maidCache != null && !string.IsNullOrEmpty(maidCache.fullName)
                    ? maidCache.fullName
                    : "メイド" + (layer.slotNo + 1);
                name += " (" + maidName + ")";
            }
            return name;
        }

        /// <summary>
        /// ボーンメニュー上部 (フレーム番号バーと同じ高さの空き領域) にレイヤー行を描く。
        /// 選択コンボ + 削除 + 追加をメニュー幅いっぱいに並べる
        /// </summary>
        private void DrawLayerControls(GUIView view, int menuWidth)
        {
            var layerType = currentLayer.layerType;

            view.currentPos.x = 0;
            view.currentPos.y = 0;

            // メニュー幅が極端に狭くてもボタンが負座標へ回り込まないよう下限を設ける
            var comboWidth = Mathf.Max(LAYER_BUTTON_WIDTH, menuWidth - LAYER_BUTTON_WIDTH * 2);

            _displayLayerComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
            _displayLayerComboBox.items = timelineManager.layers;
            // DrawListView のアクセント色でアクティブレイヤーを示す
            _displayLayerComboBox.currentIndex = timelineManager.layers.IndexOf(timelineManager.currentLayer);
            _displayLayerComboBox.defaultName = GetLayerComboLabel();
            _displayLayerComboBox.DrawButton(view);

            view.currentPos.x = comboWidth;
            view.currentPos.y = 0;
            if (view.DrawButton("-", LAYER_BUTTON_WIDTH, FRAME_LABEL_HEIGHT,
                    layerType != typeof(MTEP.MotionTimelineLayer)))
            {
                timelineManager.RemoveLayers(layerType);
            }

            view.currentPos.x = comboWidth + LAYER_BUTTON_WIDTH;
            view.currentPos.y = 0;
            _addLayerComboBox.currentIndex = -1;
            // 現在のメイドでまだ使っていない型を列挙する (スロット無しレイヤーは存在チェックのみ)。
            // 旧レイヤーコンボが担っていた「型選択で現在メイドのインスタンスを自動生成する」導線の代替
            _addableLayerInfoList.Clear();
            foreach (var info in timelineManager.layerInfoList)
            {
                if (timelineManager.GetLayer(info.layerType, maidManager.maidSlotNo) == null)
                {
                    _addableLayerInfoList.Add(info);
                }
            }
            _addLayerComboBox.items = _addableLayerInfoList;
            _addLayerComboBox.DrawButton(view);
        }

        /// <summary>コンボのボタン面ラベル。アクティブレイヤー名 + 他に表示中があれば「他N」</summary>
        private string GetLayerComboLabel()
        {
            var visibleCount = 0;
            foreach (var layer in timelineManager.layers)
            {
                if (_rowState.IsVisible(layer, currentLayer))
                {
                    visibleCount++;
                }
            }

            var name = GetLayerDisplayName(currentLayer);
            return visibleCount > 1 ? name + " 他" + (visibleCount - 1) : name;
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

            view.Init(local.x, local.y, menuWidth, local.height);
            view.SetEnabled(guiEnabled);

            view.BeginLayout(GUIView.LayoutDirection.Free);
            view.padding = Vector2.zero;

            var frameHeight = tc.frameHeight;

            DrawLayerControls(view, menuWidth);

            // ボーンメニューの表示
            view.currentPos.x = 0;
            view.currentPos.y = FRAME_LABEL_HEIGHT;
            view.DrawTexture(texWhite, menuWidth, -1, timelineLabelBgColor);

            view.scrollPosition.y = timelineView.scrollPosition.y;
            var contentWidth = menuWidth;
            var contentHeight = _rows.Count * frameHeight;
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

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                view.currentPos.y = i * frameHeight;
                if (view.currentPos.y < scrollPosition.y ||
                    view.currentPos.y > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var isActiveLayerRow = row.layer == timelineManager.currentLayer;

                // レイヤーカテゴリ行: 折りたたみトグル + レイヤー名 (クリックでアクティブ化)
                if (row.isHeader)
                {
                    var headerColor = isActiveLayerRow ? tc.timelineMenuSelectTextColor : Color.white;
                    var headerLayer = row.layer;

                    view.currentPos.x = 0;
                    view.DrawLabel(
                        _rowState.IsCollapsed(headerLayer) ? "＋" : "ー",
                        20,
                        20,
                        headerColor,
                        null,
                        () =>
                        {
                            _rowState.ToggleCollapsed(headerLayer);
                        }
                    );

                    view.currentPos.x = 20;
                    view.DrawLabel(
                        "■ " + GetLayerDisplayName(headerLayer),
                        menuWidth - 20,
                        20,
                        headerColor
                    );

                    view.InvokeActionOnEvent(
                        menuWidth - 40,
                        20,
                        EventType.MouseDown,
                        (pos) =>
                        {
                            if (headerLayer != timelineManager.currentLayer)
                            {
                                timelineManager.SetCurrentLayer(headerLayer);
                            }
                        });

                    continue;
                }

                var menuItem = row.menuItem;

                var diplayName = menuItem.displayName;
                // 選択ハイライトはアクティブレイヤーの行にだけ意味を持つ
                var isSelected = isActiveLayerRow && menuItem.isSelectedMenu;

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
                        // 非アクティブレイヤーの行はまずアクティブ化してから選択する
                        if (row.layer != timelineManager.currentLayer)
                        {
                            timelineManager.SetCurrentLayer(row.layer);
                        }
                        menuItem.SelectMenu(isMultiSelect);
                    });

                // A/D ボタンはアクティブレイヤーの行のみ (編集はアクティブレイヤーに束縛)
                if (studioHackManager.isPoseEditing && isActiveLayerRow)
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

            // メニュー幅の変更ボタン (下のカーブツールバーと重ならないようボーンメニュー下端に置く)
            view.currentPos.x = view.viewRect.width - 20;
            view.currentPos.y = curvePaneTop - 20;

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

            // カーブエディタのツールバー (ボーンメニュー下の左カラム)
            var curveEditor = TimelineCurveEditor.instance;
            var toolbarTop = curvePaneTop;
            var toolbarHeight = _contentHeight - toolbarTop;
            if (toolbarHeight > 0)
            {
                curveEditor.DrawSideToolbar(view, new Rect(0, toolbarTop, menuWidth, toolbarHeight));
            }

            view.EndLayout();
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                requestUpdateTexture = true;
            }
        }
    }
}
