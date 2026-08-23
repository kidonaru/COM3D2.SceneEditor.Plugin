using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 保存済みタイムラインのロード用ウィンドウ。
    /// サムネイル付きのタイル一覧からフォルダを辿って開く。
    /// TimelineWindow のロードコンボ (素早く選ぶ導線) とは併存させる
    /// </summary>
    public class TimelineLoadWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903386;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムラインロード";

        private static readonly int ROW_HEIGHT = 20;
        /// <summary>タイルの幅。高さはサムネの縦横比から決める</summary>
        private static readonly int TILE_WIDTH = 120;
        /// <summary>ヘッダー右端に並べる「開く」「更新」ボタン 2 つ分の幅</summary>
        private static readonly int HEADER_BUTTON_AREA_WIDTH = 110;

        private readonly GUIView _view = new GUIView();

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static TimelineLoadWindow _instance = null;
        public static TimelineLoadWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineLoadWindow();
                }
                return _instance;
            }
        }

        private TimelineLoadWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineLoadPosX;
            y = config.timelineLoadPosY;
            width = config.timelineLoadWidth;
            height = config.timelineLoadHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineLoadPosX = x;
            config.timelineLoadPosY = y;
            config.timelineLoadWidth = width;
            config.timelineLoadHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineLoadVisible;
            set => config.timelineLoadVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            DrawHeader(TimelineLoadManager.GetOrLoadCurrentDirItem());

            // ヘッダーの「更新」やフォルダ移動で差し替わるため、描画直前に取り直す
            DrawTiles(TimelineLoadManager.currentDirItem);
        }

        /// <summary>階層移動・フォルダを開く・一覧の更新</summary>
        private void DrawHeader(TimelineLoadItem currentDirItem)
        {
            _view.BeginHorizontal();
            {
                var parent = currentDirItem.parent as TimelineLoadItem;
                if (_view.DrawButton("<", 20, ROW_HEIGHT, parent != null))
                {
                    TimelineLoadManager.currentDirItem = parent;
                }

                _view.DrawLabel(currentDirItem.name, -1, ROW_HEIGHT);

                _view.currentPos.x = _view.viewRect.width - HEADER_BUTTON_AREA_WIDTH;

                if (_view.DrawButton("開く", 50, ROW_HEIGHT))
                {
                    MTEUtils.OpenDirectory(currentDirItem.path);
                }

                if (_view.DrawButton("更新", 50, ROW_HEIGHT))
                {
                    TimelineLoadManager.Reload();
                }
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);
        }

        /// <summary>タイル一覧。フォルダはクリックで移動、タイムラインはクリックでロードする</summary>
        private void DrawTiles(TimelineLoadItem currentDirItem)
        {
            if (currentDirItem.children == null || currentDirItem.children.Count == 0)
            {
                _view.DrawLabel("タイムラインがありません", -1, ROW_HEIGHT);
                return;
            }

            TimelineLoadItem selectedItem = null;
            TimelineLoadItem mouseOverItem = null;

            // 下部にマウスオーバー中の名前表示行を残し、残りをタイルへ充てる
            var tileViewHeight = _view.viewRect.height - _view.currentPos.y
                - ROW_HEIGHT - GUIView.defaultMargin;
            // 名前ラベルの分 (ROW_HEIGHT) をサムネの縦横比に足す
            var tileHeight = TILE_WIDTH * timelineConfig.thumHeight / timelineConfig.thumWidth
                + ROW_HEIGHT;

            _view.DrawTileView(
                currentDirItem,
                -1,
                tileViewHeight,
                TILE_WIDTH,
                tileHeight,
                item =>
                {
                    selectedItem = item as TimelineLoadItem;
                },
                item =>
                {
                    mouseOverItem = item as TimelineLoadItem;
                });

            OpenItem(selectedItem);

            _view.DrawBox(-1, ROW_HEIGHT);

            if (mouseOverItem != null)
            {
                _view.DrawLabel(mouseOverItem.name, -1, ROW_HEIGHT);
            }
        }

        /// <summary>フォルダなら移動、タイムラインならロードする</summary>
        private void OpenItem(TimelineLoadItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.isDir)
            {
                TimelineLoadManager.currentDirItem = item;
                return;
            }

            timelineManager.LoadTimeline(item.name, TimelineLoadManager.GetRelativeDirectoryName(item));
        }
    }
}
