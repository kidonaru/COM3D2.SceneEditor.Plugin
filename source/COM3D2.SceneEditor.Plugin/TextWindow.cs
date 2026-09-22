using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// フリーテキストの管理ウィンドウ。
    /// 表示数の増減 (番号タブの追加・削除) と、
    /// 選択したテキストの内容・スタイル・枠 Transform の編集を行う。
    /// 編集 UI の実体は TextRowDrawer (書き込み先は TimelineTextManager の FreeTextSet)。
    /// タイムライン未読込時も使用できる (実体はウィンドウ表示中に生成される)
    /// </summary>
    public class TextWindow : EditorSubWindow
    {
        /// <summary>操作対象タブのラベル幅 (「テキスト」が収まる幅)</summary>
        private const float TargetLabelWidth = 70f;

        public static readonly int WINDOW_ID = 8903394;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "テキスト";

        private static readonly int ROW_HEIGHT = 20;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.TimelineTextManager textManager => MTEP.TimelineTextManager.instance;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        /// <summary>操作対象のテキスト添字</summary>
        private int _textIndex = 0;

        // コンボ開閉状態をテキストごとに分けるため添字ベースの項目名で引く
        // (表示数上限 16 なので減った分の掃除はしない)
        private readonly ItemRowDrawerCache<TextRowDrawer> _textRowDrawers =
            new ItemRowDrawerCache<TextRowDrawer>();

        private static TextWindow _instance = null;
        public static TextWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TextWindow();
                }
                return _instance;
            }
        }

        private TextWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.textPosX;
            y = config.textPosY;
            width = config.textWidth;
            height = config.textHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.textPosX = x;
            config.textPosY = y;
            config.textWidth = width;
            config.textHeight = height;
        }

        public override bool savedVisible
        {
            get => config.textVisible;
            set => config.textVisible = value;
        }

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            return layerType == typeof(MTEP.TextTimelineLayer);
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            try
            {
                DrawBody();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawBody()
        {
            TimelineLayerGate.Begin(_view, typeof(MTEP.TextTimelineLayer), ROW_HEIGHT);

            _view.SetEnabled(_view.focusedComboBox == null);

            DrawTextTargetRow();

            // タイムライン未読込時は実体を作る経路がレイヤーに無いため、ウィンドウ表示中に直接補う
            // (テキストの初期値は空文字列なので、作られても画面には何も出ない)
            if (timeline == null && textManager.TextData.Length != textManager.textCount)
            {
                textManager.InitTexts();
            }

            if (!textManager.IsValidIndex(_textIndex))
            {
                // 表示数変更の反映はマネージャの更新タイミング待ちになる (レイヤー側と同じ扱い)
                _view.DrawLabel("テキストが見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.gray);
                return;
            }

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            _view.BeginAutoEditMode();

            // 直前キーの参照と色ピッカーの同定に使うため、レイヤーの項目名と同じ名前を渡す
            var boneName = MTEP.TextTimelineLayer.TextBoneName + _textIndex;

            _textRowDrawers.Get(boneName)
                .Draw(_view, textManager.GetFreeTextSet(_textIndex), ROW_HEIGHT, boneName,
                    _textIndex);

            _view.EndAutoEditMode();
            _view.EndScrollView();
        }

        /// <summary>
        /// 操作対象の番号タブ行。「追加」「削除」は表示数の増減で、
        /// 削除は選択中ではなく末尾のテキストを減らす
        /// </summary>
        private void DrawTextTargetRow()
        {
            var textCount = textManager.textCount;

            TargetTabsDrawer.Draw(
                _view, "テキスト", textCount, ref _textIndex, ROW_HEIGHT,
                onAdd: () => SetTextCount(textCount + 1),
                onRemove: () => SetTextCount(textCount - 1),
                canAdd: textCount < MTEP.TimelineTextManager.MaxTextCount,
                canRemove: textCount > MTEP.TimelineTextManager.MinTextCount,
                labelWidth: TargetLabelWidth);
        }

        /// <summary>
        /// テキスト表示数を変更する。
        /// テキストレイヤーがあれば LateUpdate 側が実体とメニュー項目をまとめて作り直す。
        /// ここで先に InitTexts を呼ぶとその追随ガード
        /// (TextData.Length != textCount) が空振りしてメニュー項目が更新されないため、
        /// レイヤーが無いとき (タイムライン未読込含む) だけ実体の再生成を補う
        /// </summary>
        private static void SetTextCount(int count)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Text, "テキスト: 表示数", null,
                () => TextSnapshot.Capture());

            textManager.textCount = count;

            if (timelineManager.FindLayers(typeof(MTEP.TextTimelineLayer)).Count == 0)
            {
                textManager.InitTexts();
            }
        }
    }
}
