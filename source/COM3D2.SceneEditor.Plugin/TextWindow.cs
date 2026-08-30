using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// フリーテキストの管理ウィンドウ。
    /// 表示数の増減と選択したテキストの内容・スタイル・枠 Transform の編集を行う。
    /// 編集 UI の実体は TextRowDrawer (書き込み先は TimelineTextManager の FreeTextSet)。
    /// テキストはタイムライン文脈でのみ生成・更新されるため、
    /// タイムライン未読込時は使えない (TextItemInspector と同じ制約)
    /// </summary>
    public class TextWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903394;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "テキスト";

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>
        /// テキスト表示数の範囲。MTE 側に対応する定数はなく、
        /// TimelineSettingWindow の旧・要素数行から引き継いだ SE 独自の UI 制約
        /// </summary>
        private const int MinTextCount = 1;
        private const int MaxTextCount = 16;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.TimelineTextManager textManager => MTEP.TimelineTextManager.instance;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        /// <summary>操作対象のテキスト添字</summary>
        private int _textIndex = 0;

        private readonly GUIComboBox<int> _textComboBox = new GUIComboBox<int>
        {
            getName = (index, _) => "テキスト" + index,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        /// <summary>コンボ選択肢 (0〜textCount-1)。表示数変更時だけ作り直す</summary>
        private readonly List<int> _textIndexItems = new List<int>();

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
            _textComboBox.onSelected = (index, _) => _textIndex = index;
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

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どこに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            DrawBody();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawBody()
        {
            if (timeline == null)
            {
                _view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.SetEnabled(_view.focusedComboBox == null);

            DrawTextCountRow();

            _textIndex = Mathf.Clamp(_textIndex, 0, timeline.textCount - 1);

            if (_textIndexItems.Count != timeline.textCount)
            {
                _textIndexItems.Clear();
                for (var i = 0; i < timeline.textCount; i++)
                {
                    _textIndexItems.Add(i);
                }
            }

            _textComboBox.items = _textIndexItems;
            _textComboBox.currentIndex = _textIndex;
            _textComboBox.DrawButton("操作対象", _view);

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

            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (レイヤー UI と同じ制約)
            if (!studioHackManager.isPoseEditing)
            {
                _view.DrawLabel("編集モード中のみテキストを操作できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
            }
            _view.SetEnabled(_view.focusedComboBox == null && studioHackManager.isPoseEditing);

            // 直前キーの参照と色ピッカーの同定に使うため、レイヤーの項目名と同じ名前を渡す
            var boneName = MTEP.TextTimelineLayer.TextBoneName + _textIndex;

            _textRowDrawers.Get(boneName)
                .Draw(_view, textManager.GetFreeTextSet(_textIndex), ROW_HEIGHT, boneName);

            _view.SetEnabled(_view.focusedComboBox == null);
            _view.EndScrollView();
        }

        /// <summary>テキスト表示数の増減行</summary>
        private void DrawTextCountRow()
        {
            CountRowDrawer.Draw(_view, "テキスト表示数", ROW_HEIGHT,
                timeline.textCount, MinTextCount, MaxTextCount, SetTextCount);
        }

        /// <summary>
        /// テキスト表示数を変更する。
        /// テキストレイヤーがあれば LateUpdate 側が実体とメニュー項目をまとめて作り直す。
        /// ここで先に InitTexts を呼ぶとその追随ガード
        /// (TextData.Length != textCount) が空振りしてメニュー項目が更新されないため、
        /// レイヤー未追加のタイムラインでも反映されるよう実体の再生成だけを補う
        /// </summary>
        private static void SetTextCount(int count)
        {
            timeline.textCount = count;

            if (timelineManager.FindLayers(typeof(MTEP.TextTimelineLayer)).Count == 0)
            {
                textManager.InitTexts();
            }
        }
    }
}
