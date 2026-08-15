using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 操作履歴の一覧ウィンドウ。項目クリックでその操作の直後の状態まで戻す/進める。
    /// undo/redo キーでも同じ履歴を操作する
    /// </summary>
    public class HistoryWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903372;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "操作履歴";

        private static readonly int ROW_HEIGHT = 20;

        private readonly GUIView _view = new GUIView();

        private static HistoryWindow _instance = null;
        public static HistoryWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HistoryWindow();
                }
                return _instance;
            }
        }

        private HistoryWindow()
        {
        }

        private static HistoryManager historyManager => HistoryManager.instance;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.historyPosX;
            y = config.historyPosY;
            width = config.historyWidth;
            height = config.historyHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.historyPosX = x;
            config.historyPosY = y;
            config.historyWidth = width;
            config.historyHeight = height;
        }

        public override bool savedVisible
        {
            get => config.historyVisible;
            set => config.historyVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            DrawButtonRow();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            DrawHistoryList();
        }

        /// <summary>undo/redo/クリアの操作行。ボタン名にキー割当も示す</summary>
        private void DrawButtonRow()
        {
            _view.BeginHorizontal();
            {
                if (_view.DrawButton(
                    "戻す (" + config.GetKeyName(KeyBindType.Undo) + ")",
                    100, ROW_HEIGHT, historyManager.canUndo))
                {
                    historyManager.Undo();
                }
                if (_view.DrawButton(
                    "進める (" + config.GetKeyName(KeyBindType.Redo) + ")",
                    100, ROW_HEIGHT, historyManager.canRedo))
                {
                    historyManager.Redo();
                }
                if (_view.DrawButton("クリア", 60, ROW_HEIGHT,
                    historyManager.entries.Count > 0))
                {
                    historyManager.ClearHistory();
                }
            }
            _view.EndLayout();
        }

        /// <summary>履歴一覧 (新しい順)。クリックでその操作の直後の状態へジャンプする</summary>
        private void DrawHistoryList()
        {
            var entries = historyManager.entries;
            if (entries.Count == 0)
            {
                _view.DrawLabel("履歴はありません", -1, ROW_HEIGHT);
                return;
            }

            // 表示は新しい順のため、内部 index との変換は「末尾からの位置」で行う
            var currentViewIndex = entries.Count - 1 - historyManager.currentIndex;
            var selected = _view.DrawListView(
                entries,
                (entry, i) => entries[entries.Count - 1 - i].description,
                null,
                -1, -1,
                currentViewIndex,
                ROW_HEIGHT);

            if (selected >= 0)
            {
                historyManager.RestoreTo(entries.Count - 1 - selected);
            }
        }
    }
}
