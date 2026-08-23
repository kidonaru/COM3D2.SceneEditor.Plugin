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
        }

        /// <summary>共通設定 (タイムライン全体で共有する設定) の描画</summary>
        private void DrawCommonSetting(GUIView view)
        {
        }
    }
}
