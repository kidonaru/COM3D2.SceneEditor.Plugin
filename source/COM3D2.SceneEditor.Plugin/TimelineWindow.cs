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
        /// <summary>レイヤー操作ボタン (削除 / 追加コンボ) の幅</summary>
        private static readonly int LAYER_BUTTON_WIDTH = 20;
        /// <summary>表示モード切替ボタンのアイコン余白 (TimelineControlWindow.ICON_TOGGLE_OFFSET と同じ値)</summary>
        private static readonly float MODE_ICON_OFFSET = 4f;
        /// <summary>レイヤーカテゴリ行に対するボーンメニュー行の字下げ幅</summary>
        private static readonly int MENU_INDENT_WIDTH = 10;
        /// <summary>折りたたみトグルの列幅。記号と後ろの文字が離れないよう記号幅に詰めている</summary>
        private static readonly int FOLD_TOGGLE_WIDTH = 14;
        /// <summary>折りたたみ一括操作ボタンのサイズ (文字ラベルが収まる幅)</summary>
        private static readonly int FOLD_ALL_BUTTON_WIDTH = 70;
        private static readonly int FOLD_ALL_BUTTON_HEIGHT = 20;
        /// <summary>メニュー幅が狭いときでもボタンが潰れないための下限幅</summary>
        private static readonly int FOLD_ALL_BUTTON_MIN_WIDTH = 20;
        /// <summary>レイヤーの区切り線の太さ</summary>
        private static readonly int LAYER_SEPARATOR_HEIGHT = 1;
        /// <summary>ボーンメニュー右下のメニュー幅変更ボタンのサイズ</summary>
        private static readonly int RESIZE_BUTTON_SIZE = 20;
        // 折りたたみトグルの記号。開いた状態の ▼ を右へ回した ▶ で閉じた状態を示す
        private static readonly string FOLD_OPEN = "▼";
        private static readonly string FOLD_CLOSED = "▶";

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

        private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
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
        /// <summary>フレーム番号バー (シークバー) のドラッグ状態</summary>
        private readonly GUIView.DragInfo _seekDragInfo = new GUIView.DragInfo();
        private Rect areaDragRect = new Rect();
        private readonly GUIView.DragInfo _menuWidthDraggableInfo = new GUIView.DragInfo();

        /// <summary>レイヤーモードの選択コンボ。操作対象レイヤーから 1 つ選んでアクティブ化する</summary>
        private readonly GUIComboBox<MTEP.ITimelineLayer> _layerComboBox = new GUIComboBox<MTEP.ITimelineLayer>
        {
            contentSize = new Vector2(200, 300),
            // menuWidth (100〜300px) に収めるため前後送りの矢印は省略する
            showArrow = false,
        };

        /// <summary>カテゴリモードの選択コンボ。カテゴリを選ぶとその先頭レイヤーをアクティブ化する</summary>
        private readonly GUIComboBox<MTEP.TimelineLayerCategory> _categoryComboBox
            = new GUIComboBox<MTEP.TimelineLayerCategory>
        {
            contentSize = new Vector2(200, 300),
            // 項目数が少なく前後送りで十分たどれるので < > の矢印を出す
            // (メニュー幅が狭いときは DrawLayerControls で畳む)
            showArrow = true,
        };

        /// <summary>カテゴリの列挙順 (enum 定義順)</summary>
        private static readonly List<MTEP.TimelineLayerCategory> ALL_CATEGORIES =
            MTEUtils.GetEnumValues<MTEP.TimelineLayerCategory>();

        /// <summary>カテゴリごとの操作対象レイヤー数。BuildTargetLayers で詰め直す</summary>
        private readonly int[] _categoryLayerCounts = new int[ALL_CATEGORIES.Count];

        /// <summary>操作対象レイヤーが 1 件以上あるカテゴリ (コンボの項目)。BuildTargetLayers で詰め直す</summary>
        private readonly List<MTEP.TimelineLayerCategory> _availableCategories
            = new List<MTEP.TimelineLayerCategory>(ALL_CATEGORIES.Count);

        /// <summary>今フレームの表示対象レイヤー (_targetLayers を表示モードで絞ったもの)。DrawBody で詰め直す</summary>
        private readonly List<MTEP.ITimelineLayer> _displayLayers
            = new List<MTEP.ITimelineLayer>(32);

        /// <summary>レイヤーの展開集合 (セッション内のみ保持)</summary>
        private readonly TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem> _rowState
            = new TimelineLayerRowState<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>(GetLayerStateKey);

        /// <summary>
        /// 今フレームの操作対象レイヤー (他メイドのレイヤーを除いたもの)。DrawBody で詰め直す。
        /// 表示対象 (_displayLayers) はこれを表示モードで絞ったもの
        /// </summary>
        private readonly List<MTEP.ITimelineLayer> _targetLayers
            = new List<MTEP.ITimelineLayer>(32);

        /// <summary>今フレームの表示行 (カテゴリ行 + ボーンメニュー行)。DrawBody で再構築する</summary>
        private readonly List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>> _rows
            = new List<LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem>>(256);

        /// <summary>タイムライン切替検知用。新規作成・読み込みで表示状態をリセットする</summary>
        private int _lastTimelineSessionId = -1;

        /// <summary>タイムライン再構築の検知用。レイヤーが別インスタンスになったら Prune する</summary>
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
            },
            defaultName = "+",
            buttonSize = new Vector2(LAYER_BUTTON_WIDTH, FRAME_LABEL_HEIGHT),
            contentSize = new Vector2(150, 300),
            showArrow = false,
        };

        /// <summary>
        /// キー入力処理を TimelineKeyInput 側に移したため、キャッシュせず都度参照する。
        /// 旧実装と異なりテキスト入力中も実キー状態を反映する
        /// </summary>
        private bool isMultiSelect =>
            config.isTimelineKeyInputEnabled && config.GetKey(KeyBindType.MultiSelect);

        private Texture2D texWhite => GUIView.texWhite;
        private Texture2D texTimelineBG = null;
        private Texture2D texKeyFrame = null;
        /// <summary>色レーンのキー間グラデーション用 (左透明→右不透明)</summary>
        private Texture2D texColorGradient = null;

        /// <summary>色レーンの帯がレーン高さに占める割合</summary>
        private const float COLOR_LANE_HEIGHT_RATIO = 0.3f;
        /// <summary>色レーンの不透明度。背景の目盛りとキーフレームが埋もれない程度に抑える</summary>
        private const float COLOR_LANE_ALPHA = 0.7f;
        private const int COLOR_GRADIENT_TEXTURE_WIDTH = 64;

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
            HistoryManager.instance.onEditCommitted += OnEditCommitted;

            // フィールド初期化子ではインスタンスメンバーを参照できないためここで設定する
            // ドロップダウンは操作対象で絞った一覧なのでメイド名は省く
            _layerComboBox.getName = (layer, _) => GetLayerDisplayName(layer, false);
            _layerComboBox.onSelected = (layer, _) =>
            {
                if (layer != timelineManager.currentLayer)
                {
                    timelineManager.SetCurrentLayer(layer);
                }
            };

            _categoryComboBox.getName = (category, _) => GetCategoryLabel(category);
            _categoryComboBox.onSelected = (category, _) =>
            {
                var first = TimelineLayerViewFilter.FindFirstLayer(
                    _targetLayers, category, GetLayerCategory, GetLayerPriority);
                if (first != null && first != timelineManager.currentLayer)
                {
                    timelineManager.SetCurrentLayer(first);
                }
            };
        }

        // ドラッグ編集完了時 (SE 独自機能)
        private void OnDragCompleted(Maid maid)
        {
            // ビューポートでのボーン操作はレイヤーゲートの外で起きるため、
            // 触ったレイヤー (そのメイドのメイドアニメ) を明示的に控える
            var maidCache = maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            if (maidCache != null)
            {
                TimelineLayerGate.RecordEditedLayer(
                    typeof(MTEP.MotionTimelineLayer), maidCache.slotNo);
            }

            HandleEditFinished(maid);
        }

        // 各ウィンドウでの値変更が操作履歴として確定したとき。
        // ドラッグ編集は onDragCompleted と両方から届くが、2 回目は登録済みボーンが除かれて no-op になる
        private void OnEditCommitted(HistoryEntry entry)
        {
            HandleEditFinished(entry.maid);
        }

        /// <summary>
        /// 編集の確定を受けて、値を変えたレイヤーへアクティブを移し、自動登録を試みる。
        /// メイドに紐づかない操作 (ライト・カメラ等) は editedMaid が null で届く
        /// </summary>
        private void HandleEditFinished(Maid editedMaid)
        {
            // 破棄済みメイドは Unity の == では null 扱いだが object の参照比較では null にならず、
            // メイドに紐づかない操作と取り違えられる。純粋ロジックへ渡す前にここで弾く
            if (!ReferenceEquals(editedMaid, null) && editedMaid == null)
            {
                return;
            }

            var isEditing = timelineManager.currentLayer != null
                && timelineManager.initialEditFrame != null;

            // レイヤーの自動追従は「自動登録」トグルに依らず働かせる。
            // 控えは毎回必ず消したいので、条件を付けずに呼ぶ
            FocusEditedLayer();

            TryAutoKeyFrame(editedMaid, isEditing);
        }

        /// <summary>
        /// 自動登録が有効で編集モード中なら、現在フレームへ差分をキーフレーム登録する。
        /// 指ドラッグ等は選択同期を経ずアクティブメイドが別メイドのままになり得るため、
        /// 操作対象メイドが登録対象 (アクティブメイド) と一致する場合のみ登録する
        /// </summary>
        private void TryAutoKeyFrame(Maid editedMaid, bool isEditing)
        {
            if (!AutoKeyFrameGate.ShouldRegister(
                isAutoKeyFrame: MTEP.ConfigManager.instance.config.isAutoKeyFrame,
                isEditing: isEditing,
                editedMaid: editedMaid,
                activeMaid: MTEP.MaidManager.instance.maid))
            {
                return;
            }

            MTEP.TimelineManager.instance.AddKeyFrameDiff(isAuto: true);
        }

        /// <summary>
        /// 値を変えたレイヤーをアクティブにする。
        /// 登録は表示中のレイヤーだけが対象なので、先に切り替えておくことで
        /// 「触ったものにキーが入る」が成り立つ。
        /// カメラ系は対象外 (アクティブな間はカメラ同期などの操作が塞がるため)
        /// </summary>
        private static void FocusEditedLayer()
        {
            int slotNo;
            var layerType = TimelineLayerGate.TakeEditedLayer(out slotNo);
            if (layerType == null)
            {
                return;
            }

            var layer = timelineManager.GetLayer(layerType, slotNo);
            if (layer == null || layer == timelineManager.currentLayer || layer.isCameraLayer)
            {
                return;
            }

            // 編集開始時スナップショットが無いレイヤー (操作対象メイド以外のもの等) へ移ると、
            // currentLayer と initialEditFrame が食い違ったまま残る
            if (timelineManager.GetInitialEditFrame(layer) == null)
            {
                return;
            }

            // SetCurrentLayer だと編集セッションが張り直され、
            // まだ登録していない編集値が保存済みキー値で上書きされる
            timelineManager.SetCurrentLayerKeepingEdit(layer);
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
                                // 選択に追従するだけなのでレイヤーは作らない
                                timelineManager.ChangeActiveLayerForSlot(i);
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
                        // 選択に追従するだけなのでレイヤーは作らない
                        timelineManager.ChangeActiveLayerIfExists(typeof(MTEP.LightTimelineLayer));
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

            if (texColorGradient == null)
            {
                texColorGradient = TextureUtils.CreateHorizontalAlphaGradientTexture(
                    COLOR_GRADIENT_TEXTURE_WIDTH);
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
                            && timeline != null
                            && maidManager.maid != null;

            bool guiEnabled = contentView.focusedComboBox == null;

            // タイムラインを切り替えたら表示状態を初期化 (全表示・全折りたたみ)。
            // Undo/Redo もタイムラインを作り直すが、そこで初期化すると巻き戻すたびに
            // 展開状態が失われるため、インスタンスではなくセッション番号で判定する
            if (timelineManager.timelineSessionId != _lastTimelineSessionId)
            {
                _lastTimelineSessionId = timelineManager.timelineSessionId;
                _rowState.Reset();
            }

            if (editEnabled)
            {
                // レイヤーの追加・削除は必ず数の変化を伴うため、Prune は数が変わったときだけで足りる。
                // 同一フレームで削除と追加が同数起きた場合は掃除が遅れるが、layers に無いので
                // 誤描画はしない。加えて Undo/Redo と読み込みはタイムラインごと作り直して
                // レイヤーを別インスタンスにするため、キーキャッシュを貼り直しに行く
                if (timeline != _lastTimeline || timelineManager.layers.Count != _lastLayerCount)
                {
                    _lastTimeline = timeline;
                    _lastLayerCount = timelineManager.layers.Count;
                    _rowState.Prune(timelineManager.layers);
                }
                BuildTargetLayers();
                BuildRows();
            }

            if (texTimelineBG == null && editEnabled)
            {
                UpdateTexture();
            }

            DrawTimeline(local, editEnabled, guiEnabled);
            DrawBoneMenu(local, editEnabled, guiEnabled);
        }

        /// <summary>
        /// タイムラインに出すレイヤーを詰め直す。メイドに紐づくレイヤーは
        /// タイムライン操作ウィンドウで選んだ操作対象のものだけに絞る
        /// (スロットを持たないカメラ・背景等は常に対象)。
        /// あわせてカテゴリ別の件数と、表示モードで絞った表示対象も組み立てる
        /// </summary>
        private void BuildTargetLayers()
        {
            _targetLayers.Clear();
            Array.Clear(_categoryLayerCounts, 0, _categoryLayerCounts.Length);

            var slotNo = maidManager.maidSlotNo;
            foreach (var layer in timelineManager.layers)
            {
                // アクティブレイヤーは操作対象と食い違っても常に出す (行が消えて編集不能になるのを防ぐ)
                if (!layer.hasSlotNo || layer.slotNo == slotNo || layer == currentLayer)
                {
                    _targetLayers.Add(layer);
                    _categoryLayerCounts[(int)GetLayerCategory(layer)]++;
                }
            }

            _availableCategories.Clear();
            foreach (var category in ALL_CATEGORIES)
            {
                if (_categoryLayerCounts[(int)category] > 0)
                {
                    _availableCategories.Add(category);
                }
            }

            TimelineLayerViewFilter.Filter(
                _targetLayers, currentLayer, timelineConfig.layerViewMode, GetLayerCategory, _displayLayers);
        }

        /// <summary>表示対象レイヤーから表示行リストを組み立てる</summary>
        private void BuildRows()
        {
            _rowState.BuildRows(_displayLayers, CollectVisibleItems, _rows);
        }

        private void CollectVisibleItems(MTEP.ITimelineLayer layer, List<MTEP.IBoneMenuItem> result)
        {
            boneMenuManager.GetVisibleItems(layer, result);
        }

        /// <summary>行のキーフレームを選択する。ヘッダー行 (集約表示) はフレーム内の全ボーンをまとめて選択する</summary>
        private void SelectRowFrame(
            LayerRow<MTEP.ITimelineLayer, MTEP.IBoneMenuItem> row,
            MTEP.FrameData frame,
            bool isMultiSelect)
        {
            if (row.isHeader)
            {
                timelineManager.SelectBones(frame.bones.ToList(), isMultiSelect);
            }
            else
            {
                row.menuItem.SelectFrame(frame, isMultiSelect);
            }
        }

        /// <summary>フレーム内に選択中のボーンがあるか。描画ループ用に LINQ（デリゲート生成）を避けている</summary>
        private bool HasSelectedBone(MTEP.FrameData frame)
        {
            foreach (var bone in frame.bones)
            {
                if (timelineManager.IsSelectedBone(bone))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>選択中のボーンから指定フレーム番号のものを 1 つ返す。無ければ null</summary>
        private MTEP.BoneData FindSelectedBone(int frameNo)
        {
            foreach (var bone in timelineManager.selectedBones)
            {
                if (bone.frameNo == frameNo)
                {
                    return bone;
                }
            }
            return null;
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
            bool alwaysShowVertical = true;

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

                // カテゴリ行はキーを持たないので、レイヤーの境目の区切り線と選択ハイライトのみ
                if (row.isHeader)
                {
                    if (i > 0)
                    {
                        view.DrawTexture(
                            texWhite,
                            viewWidth,
                            LAYER_SEPARATOR_HEIGHT,
                            tc.timelineLineColor1);
                    }

                    if (boneMenuManager.IsLayerMenuSelected(row.layer))
                    {
                        view.currentPos.y = i * frameHeight;
                        view.DrawTexture(
                            texWhite,
                            viewWidth,
                            frameHeight,
                            tc.timelineMenuSelectBgColor);
                    }
                    continue;
                }

                // 選択ハイライトはレイヤーをまたいで表示する
                if (row.menuItem.isSelectedMenu)
                {
                    view.DrawTexture(
                        texWhite,
                        viewWidth,
                        frameHeight,
                        tc.timelineMenuSelectBgColor);
                }
            }

            // 色レーン表示 (キーフレームより下、選択ハイライトより上)
            DrawColorLanes(view, scrollPosition, viewWidth, viewHeight, frameWidth, frameHeight);

            // BPMライン表示
            if (timeline.bgm.isShowBPMLine && timeline.bgm.bpm > 0)
            {
                var frameNoPerBeat = timeline.frameRate * 60.0 / timeline.bgm.bpm;
                var offsetFrame = timeline.bgm.bpmLineOffsetFrame;
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

            // キーフレーム表示。行リストを同一レイヤーの連続ブロックごとに走査する。
            // 選択はレイヤーをまたいで保持されるので、アクティブレイヤーは選択判定に関与しない
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

                // GC 対策: この二重ループはキーフレーム総数分（数千/frame）走るので、
                // ラムダによるクロージャ生成とリストのコピーを避ける
                // (docs/timeline-window-alloc-profiling.md)
                var keyFrameCount = blockLayer.keyFrameCount;
                for (var frameIndex = 0; frameIndex < keyFrameCount; frameIndex++)
                {
                    var frame = blockLayer.GetKeyFrameAt(frameIndex);
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

                        // ヘッダー行は折りたたみ中のみレイヤー全体のキーフレームを集約表示する
                        // (展開中は各アイテム行が表示するため重複させない)。グループヘッダーの
                        // 集約表示 (BoneSetMenuItem.HasVisibleBone) と同じ振る舞いに揃えている
                        var isHeader = row.isHeader;
                        if (isHeader && !_rowState.IsCollapsed(row.layer))
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

                        // ヘッダー行かどうかで表示・選択の判定対象が丸ごと切り替わる
                        bool hasVisible;
                        bool isSelected;
                        if (isHeader)
                        {
                            hasVisible = frame.HasBones();
                            isSelected = HasSelectedBone(frame);
                        }
                        else
                        {
                            hasVisible = menuItem.HasVisibleBone(frame);
                            isSelected = menuItem.IsSelectedFrame(frame);
                        }

                        if (!hasVisible)
                        {
                            continue;
                        }

                        var keyFrameRect = new Rect(
                                view.currentPos.x,
                                view.currentPos.y,
                                frameWidth,
                                frameWidth);

                        // エリア選択範囲内のキーフレームを選択 (全レイヤー対象)
                        if (areaDragInfo.isDragging)
                        {
                            if (areaDragRect.Overlaps(keyFrameRect))
                            {
                                if (!isSelected)
                                {
                                    SelectRowFrame(row, frame, true);
                                }
                            }
                            else
                            {
                                if (isSelected && !isMultiSelect)
                                {
                                    SelectRowFrame(row, frame, true);
                                }
                            }
                        }

                        // フレームのドラッグ開始。クリックしたレイヤーを編集基準 (アクティブ) にする
                        if (!areaDragInfo.isDragging && !frameDragInfo.isDragging &&
                            view.InvokeActionOnDragStart(keyFrameRect, frameDragInfo, view.currentPos))
                        {
                            if (row.layer != timelineManager.currentLayer)
                            {
                                timelineManager.SetCurrentLayer(row.layer);
                            }
                            SelectRowFrame(row, frame, isMultiSelect);
                            frameDragBoneData = FindSelectedBone(frameNo);

                            // 消費しないと GUI.DragWindow が拾ってウィンドウごと動いてしまう
                            Event.current.Use();
                        }

                        var keyFrameColor = isSelected ? Color.red : Color.white;

                        // ヘッダー行の集約表示は「全ボーン揃い」の判定対象が定まらないため常に通常色
                        if (!isHeader && !menuItem.IsFullBones(frame))
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

            // フレーム移動 (シークバー)。押下位置へシークし、そのままドラッグでも追従させる。
            // DragInfo.pos にバー内 X を持たせ、移動量は InvokeActionOnDragging が加算してくれる
            var seekRect = view.GetDrawRect(-1, FRAME_LABEL_HEIGHT);
            var seekStartX = Event.current.mousePosition.x - seekRect.x;
            if (view.InvokeActionOnDragStart(seekRect, _seekDragInfo, new Vector2(seekStartX, 0f)))
            {
                SeekByBarPosition(seekStartX, scrollPosition.x, frameWidth);
                // 消費しないと GUI.DragWindow が拾ってウィンドウごと動いてしまう
                Event.current.Use();
            }
            if (_seekDragInfo.isDragging)
            {
                // コールバックはマウスが動いたフレームだけ呼ばれるため、端で止めたまま
                // スクロールし続けることはない
                view.InvokeActionOnDragging(_seekDragInfo, pos =>
                {
                    SeekByBarPosition(pos.x, scrollPosition.x, frameWidth);
                    // 現在フレームが見えるように追従スクロールする
                    FixScrollPosition();
                });
            }

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
        /// 色を持つ項目の行に、キー間の色をグラデーション帯として描く。
        /// 複数の色 (主色/副色など) を持つ型は帯を上下に分割して並べる。
        /// 最後のキー以降は末尾まで同じ色を保持する。
        /// 折りたたみ中のレイヤーは項目行が無く、ヘッダー行へは集約しないため帯は出ない
        /// </summary>
        private void DrawColorLanes(
            GUIView view,
            Vector2 scrollPosition,
            float viewWidth,
            float viewHeight,
            float frameWidth,
            float frameHeight)
        {
            var halfFrameWidth = frameWidth * 0.5f;
            var laneHeight = frameHeight * COLOR_LANE_HEIGHT_RATIO;
            var laneOffsetY = (frameHeight - laneHeight) * 0.5f;
            var endX = timeline.maxFrameCount * frameWidth;
            var viewRightX = scrollPosition.x + viewWidth;

            for (var i = 0; i < _rows.Count; i++)
            {
                var rowY = i * frameHeight;
                if (rowY + frameHeight < scrollPosition.y ||
                    rowY > scrollPosition.y + viewHeight)
                {
                    continue;
                }

                var row = _rows[i];
                if (row.isHeader || row.menuItem.isSetMenu)
                {
                    continue;
                }

                var layer = row.layer;
                var boneName = row.menuItem.name;
                var keyFrameCount = layer.keyFrameCount;
                var laneY = rowY + laneOffsetY;
                MTEP.BoneData prevBone = null;
                // 表示範囲の右端を越えたキーまで描いたら残りは見えないので打ち切る
                var reachedViewRight = false;

                for (var frameIndex = 0; frameIndex < keyFrameCount && !reachedViewRight; frameIndex++)
                {
                    var frame = layer.GetKeyFrameAt(frameIndex);
                    var bone = frame.GetBone(boneName);
                    if (bone == null)
                    {
                        // このキーに項目のボーンが無ければ直前のキーからの区間を延ばす
                        continue;
                    }

                    // 色を持たない型の行はここで打ち切る (型は行内で一定)
                    if (bone.transform.GetColorValueInfoMap().Count == 0)
                    {
                        break;
                    }

                    var boneX = bone.frameNo * frameWidth + halfFrameWidth;
                    reachedViewRight = boneX > viewRightX;

                    if (prevBone != null)
                    {
                        DrawColorLaneSegment(
                            view,
                            from: prevBone.transform,
                            to: bone.transform,
                            x0: prevBone.frameNo * frameWidth + halfFrameWidth,
                            x1: boneX,
                            y: laneY,
                            height: laneHeight,
                            scrollX: scrollPosition.x,
                            viewWidth: viewWidth);
                    }
                    prevBone = bone;
                }

                if (prevBone != null && !reachedViewRight)
                {
                    DrawColorLaneSegment(
                        view,
                        from: prevBone.transform,
                        to: null,
                        x0: prevBone.frameNo * frameWidth + halfFrameWidth,
                        x1: endX,
                        y: laneY,
                        height: laneHeight,
                        scrollX: scrollPosition.x,
                        viewWidth: viewWidth);
                }
            }
        }

        /// <summary>
        /// 2 キー間の色帯を 1 区間ぶん描く。左の色を塗った上に右の色をアルファ勾配テクスチャで重ね、
        /// 線形ブレンドに見せる。to が null のときは from の色で塗りつぶす。
        /// from が表示 OFF のキーなら何も描かない
        /// </summary>
        private void DrawColorLaneSegment(
            GUIView view,
            MTEP.ITransformData from,
            MTEP.ITransformData to,
            float x0,
            float x1,
            float y,
            float height,
            float scrollX,
            float viewWidth)
        {
            var width = x1 - x0;
            if (width <= 0f || x1 < scrollX || x0 > scrollX + viewWidth)
            {
                return;
            }

            // 表示 OFF は次のキーまで保持されるので、その区間は色を出さない
            if (from.hasVisible && !from.visible)
            {
                return;
            }

            var colorMap = from.GetColorValueInfoMap();
            var bandHeight = height / colorMap.Count;
            var bandY = y;

            foreach (var pair in colorMap)
            {
                var colorKey = pair.Key;
                var fromColor = ToLaneColor(from.GetColorValue(colorKey));

                view.currentPos.x = x0;
                view.currentPos.y = bandY;
                view.DrawTexture(texWhite, width, bandHeight, fromColor);

                if (to != null)
                {
                    var toColor = ToLaneColor(to.GetColorValue(colorKey));
                    if (toColor != fromColor)
                    {
                        view.currentPos.x = x0;
                        view.currentPos.y = bandY;
                        view.DrawTexture(texColorGradient, width, bandHeight, toColor);
                    }
                }

                bandY += bandHeight;
            }
        }

        /// <summary>
        /// HDR 値を表示範囲に丸め、帯の不透明度を掛けた表示用の色にする。
        /// 1 を超える強度差は帯には反映しない (色相の推移が分かれば十分とみなす)
        /// </summary>
        private static Color ToLaneColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a) * COLOR_LANE_ALPHA);
        }

        /// <summary>
        /// 全レイヤーの折りたたみを一括で切り替えるボタン。
        /// アイコンでは意味が伝わりにくいため、押したときに起きることを文字で示す。
        /// スクロールビュー下端の空き帯 (右端の幅変更ボタンの左側) に置く
        /// </summary>
        private void DrawRowStateControls(GUIView view, MTEP.Config tc)
        {
            var layers = _displayLayers;
            var allCollapsed = _rowState.AreAllCollapsed(layers);

            view.currentPos.x = 0;
            view.currentPos.y = curvePaneTop - FOLD_ALL_BUTTON_HEIGHT;
            // メニュー幅が狭いときは右端の幅変更ボタンに重ならないよう詰める (潰れない下限も設ける)
            var buttonWidth = Mathf.Max(
                FOLD_ALL_BUTTON_MIN_WIDTH,
                Mathf.Min(FOLD_ALL_BUTTON_WIDTH, tc.menuWidth - RESIZE_BUTTON_SIZE));
            if (view.DrawButton(
                    allCollapsed ? "全て展開" : "全て畳む",
                    buttonWidth,
                    FOLD_ALL_BUTTON_HEIGHT))
            {
                _rowState.SetAllCollapsed(layers, !allCollapsed);
            }
        }

        // Filter / FindFirstLayer へメソッドグループとして渡すためのラッパー
        private static MTEP.TimelineLayerCategory GetLayerCategory(MTEP.ITimelineLayer layer)
        {
            return timelineManager.GetLayerCategory(layer);
        }

        // GetLayerInfo は未登録の型に null を返す。全レイヤー型は登録済みの前提だが、
        // GetLayerDisplayName と同じく null を握って「最後尾」へ寄せる
        private static int GetLayerPriority(MTEP.ITimelineLayer layer)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            return info != null ? info.priority : int.MaxValue;
        }

        /// <summary>カテゴリコンボの項目名。カテゴリ名 + 操作対象レイヤー数</summary>
        private string GetCategoryLabel(MTEP.TimelineLayerCategory category)
        {
            return MTEP.Extensions.ToDisplayName(category) + " (" + _categoryLayerCounts[(int)category] + ")";
        }

        /// <summary>
        /// 表示状態をレイヤーに紐づけるキー。レイヤーはクラスとスロット番号の組で
        /// 一意 (TimelineManager.GetLayer と同じ同定条件) なので、タイムラインを
        /// 作り直しても同じレイヤーには同じキーが対応する
        /// </summary>
        private static string GetLayerStateKey(MTEP.ITimelineLayer layer)
        {
            return layer.layerName + "@" + (layer.hasSlotNo ? layer.slotNo : 0);
        }

        /// <summary>
        /// レイヤーインスタンスの表示名。スロット付きレイヤーはメイド名を併記して
        /// 同型レイヤーのインスタンスを区別できるようにする
        /// (操作対象で絞り込み済みの一覧では withMaidName = false で省く)
        /// </summary>
        private string GetLayerDisplayName(MTEP.ITimelineLayer layer, bool withMaidName = true)
        {
            var info = timelineManager.GetLayerInfo(layer.layerType);
            var name = info != null ? info.displayName : layer.layerName;
            if (withMaidName && layer.hasSlotNo)
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
        /// モード切替 + 選択コンボ + 削除 + 追加をメニュー幅いっぱいに並べる
        /// </summary>
        private void DrawLayerControls(GUIView view, int menuWidth)
        {
            var layerType = currentLayer.layerType;
            var isCategoryMode = timelineConfig.layerViewMode == MTEP.TimelineLayerViewMode.Category;

            view.currentPos.x = 0;
            view.currentPos.y = 0;
            DrawViewModeButton(view, isCategoryMode);

            // コンボ (矢印込み) に割ける幅
            var comboAreaWidth = menuWidth - FRAME_LABEL_HEIGHT - LAYER_BUTTON_WIDTH * 2;
            // カテゴリコンボの前後送り矢印はコンボ本体の幅を食う。
            // 矢印を出すとコンボ本体が下限幅を割るほど狭いときは、右端からはみ出さないよう矢印を畳む
            var arrowWidth = GUIComboBoxBase.ARROW_SIZE * 2;
            var showCategoryArrow = isCategoryMode && comboAreaWidth - arrowWidth >= LAYER_BUTTON_WIDTH;
            var usedArrowWidth = showCategoryArrow ? arrowWidth : 0f;
            // メニュー幅が極端に狭くてもボタンが負座標へ回り込まないよう下限を設ける
            var comboWidth = Mathf.Max(LAYER_BUTTON_WIDTH, comboAreaWidth - usedArrowWidth);

            view.currentPos.x = FRAME_LABEL_HEIGHT;
            view.currentPos.y = 0;
            if (isCategoryMode)
            {
                _categoryComboBox.showArrow = showCategoryArrow;
                _categoryComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
                _categoryComboBox.items = _availableCategories;
                _categoryComboBox.currentIndex = _availableCategories.IndexOf(GetLayerCategory(currentLayer));
                _categoryComboBox.DrawButton(view);
            }
            else
            {
                _layerComboBox.buttonSize = new Vector2(comboWidth, FRAME_LABEL_HEIGHT);
                _layerComboBox.items = _targetLayers;
                _layerComboBox.currentIndex = _targetLayers.IndexOf(currentLayer);
                _layerComboBox.DrawButton(view);
            }

            // コンボの実描画幅は本体 + 矢印なので、後続のボタンはその分だけ右へ寄せる
            view.currentPos.x = FRAME_LABEL_HEIGHT + comboWidth + usedArrowWidth;
            view.currentPos.y = 0;
            if (view.DrawButton("-", LAYER_BUTTON_WIDTH, FRAME_LABEL_HEIGHT,
                    layerType != typeof(MTEP.MotionTimelineLayer)))
            {
                timelineManager.RemoveLayers(layerType);
            }

            view.currentPos.x = FRAME_LABEL_HEIGHT + comboWidth + usedArrowWidth + LAYER_BUTTON_WIDTH;
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

        /// <summary>
        /// 表示モードの切替ボタン。現在のモードのアイコンを出し、押すともう一方へ切り替える。
        /// アイコンテクスチャの生成に失敗した場合は 1 文字のテキストボタンにフォールバックする
        /// </summary>
        private void DrawViewModeButton(GUIView view, bool isCategoryMode)
        {
            var kind = isCategoryMode ? ToolbarIcons.Kind.CategoryMode : ToolbarIcons.Kind.LayerMode;
            var tooltip = isCategoryMode ? "カテゴリモード" : "レイヤーモード";
            var icon = ToolbarIcons.GetTexture(kind);

            var clicked = icon != null
                ? view.DrawTextureButton(icon, FRAME_LABEL_HEIGHT, FRAME_LABEL_HEIGHT, MODE_ICON_OFFSET, tooltip: tooltip)
                : view.DrawButton(isCategoryMode ? "カ" : "レ", FRAME_LABEL_HEIGHT, FRAME_LABEL_HEIGHT);
            if (!clicked)
            {
                return;
            }

            timelineConfig.layerViewMode = isCategoryMode
                ? MTEP.TimelineLayerViewMode.Layer
                : MTEP.TimelineLayerViewMode.Category;
            timelineConfig.dirty = true;
        }

        /// <summary>現在の MouseDown がダブルクリックの 2 回目か</summary>
        private static bool IsDoubleClick()
        {
            return Event.current.clickCount >= 2;
        }

        /// <summary>シークバー上の X 座標から現在フレームを決める。バー外へ出た分は端にクランプする</summary>
        private static void SeekByBarPosition(float barX, float scrollX, float frameWidth)
        {
            var frameNo = (int)((scrollX + barX) / frameWidth);
            frameNo = Mathf.Clamp(frameNo, 0, timeline.maxFrameNo);
            if (frameNo != timelineManager.currentFrameNo)
            {
                timelineManager.SeekCurrentFrame(frameNo);
            }
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

            var indent = MENU_INDENT_WIDTH;

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

                // レイヤーカテゴリ行: 折りたたみトグル + レイヤー名 (クリックでアクティブ化 + 全項目選択)
                if (row.isHeader)
                {
                    // アクティブレイヤーと全項目選択済みのレイヤーを同じ強調色で示す
                    var headerColor =
                        isActiveLayerRow || boneMenuManager.IsLayerMenuSelected(row.layer)
                            ? tc.timelineMenuSelectTextColor
                            : Color.white;
                    var headerLayer = row.layer;

                    // ドープシート側と同じ位置にレイヤーの区切り線を引く
                    if (i > 0)
                    {
                        view.currentPos.x = 0;
                        view.DrawTexture(
                            texWhite, menuWidth, LAYER_SEPARATOR_HEIGHT, tc.timelineLineColor1);
                        view.currentPos.y = i * frameHeight;
                    }

                    view.currentPos.x = 0;
                    view.DrawLabel(
                        _rowState.IsCollapsed(headerLayer) ? FOLD_CLOSED : FOLD_OPEN,
                        FOLD_TOGGLE_WIDTH,
                        20,
                        headerColor,
                        null,
                        () =>
                        {
                            _rowState.ToggleCollapsed(headerLayer);
                        }
                    );

                    view.currentPos.x = FOLD_TOGGLE_WIDTH;
                    view.DrawLabel(
                        GetLayerDisplayName(headerLayer),
                        menuWidth - FOLD_TOGGLE_WIDTH,
                        20,
                        headerColor
                    );

                    view.InvokeActionOnEvent(
                        menuWidth - FOLD_TOGGLE_WIDTH - 20,
                        20,
                        EventType.MouseDown,
                        (pos) =>
                        {
                            // レイヤー名のダブルクリックは折りたたみ切替 (記号を狙わなくてよいように)
                            if (IsDoubleClick())
                            {
                                _rowState.ToggleCollapsed(headerLayer);
                                return;
                            }
                            // クリックしたレイヤーを編集基準 (アクティブ) にしてから全項目を選択する
                            if (headerLayer != timelineManager.currentLayer)
                            {
                                timelineManager.SetCurrentLayer(headerLayer);
                            }
                            boneMenuManager.SelectLayerMenuItems(headerLayer, isMultiSelect);
                        });

                    continue;
                }

                var menuItem = row.menuItem;

                var diplayName = menuItem.displayName;
                var isSelected = menuItem.isSelectedMenu;

                view.currentPos.x = indent;

                if (menuItem.isSetMenu)
                {
                    view.DrawLabel(
                        menuItem.isOpenMenu ? FOLD_OPEN : FOLD_CLOSED,
                        FOLD_TOGGLE_WIDTH,
                        20,
                        isSelected ? tc.timelineMenuSelectTextColor : Color.white,
                        null,
                        () =>
                        {
                            menuItem.isOpenMenu = !menuItem.isOpenMenu;
                        }
                    );
                }

                view.currentPos.x = indent + FOLD_TOGGLE_WIDTH;

                view.DrawLabel(
                    diplayName,
                    menuWidth - FOLD_TOGGLE_WIDTH - indent,
                    20,
                    isSelected ? tc.timelineMenuSelectTextColor : Color.white
                );

                view.InvokeActionOnEvent(
                    menuWidth - FOLD_TOGGLE_WIDTH - indent - 20,
                    20,
                    EventType.MouseDown,
                    (pos) =>
                    {
                        // グループ名のダブルクリックは展開/折りたたみ (記号を狙わなくてよいように)
                        if (menuItem.isSetMenu && IsDoubleClick())
                        {
                            menuItem.isOpenMenu = !menuItem.isOpenMenu;
                            return;
                        }

                        // クリックしたレイヤーを編集基準 (アクティブ) にしてから選択する
                        if (row.layer != timelineManager.currentLayer)
                        {
                            timelineManager.SetCurrentLayer(row.layer);
                        }
                        menuItem.SelectMenu(isMultiSelect);
                    });

                // A/D ボタンはアクティブレイヤーの行のみ (編集はアクティブレイヤーに束縛)
                if (MTEP.SceneEditorHack.isPoseEditing && isActiveLayerRow)
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

            DrawRowStateControls(view, tc);

            // メニュー幅の変更ボタン (下のカーブツールバーと重ならないようボーンメニュー下端に置く)
            view.currentPos.x = view.viewRect.width - RESIZE_BUTTON_SIZE;
            view.currentPos.y = curvePaneTop - RESIZE_BUTTON_SIZE;

            var buttonRect = view.GetDrawRect(RESIZE_BUTTON_SIZE, RESIZE_BUTTON_SIZE);
            if (buttonRect.Contains(Event.current.mousePosition) ||
                _menuWidthDraggableInfo.isDragging)
            {
                view.DrawDraggableButton("□", RESIZE_BUTTON_SIZE, RESIZE_BUTTON_SIZE,
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
