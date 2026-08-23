using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤー固有設定を表示するウィンドウ。
    /// MTE 本家のサブウィンドウ「レイヤー情報」相当で、
    /// 現在レイヤーの DrawWindow (カメラ数値編集・ライト一覧・拡張ボーン等) を描画する
    /// </summary>
    public class TimelineLayerWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903385;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "レイヤー設定";
        protected override int minWidth => 300;
        protected override int minHeight => 300;

        private static readonly int ROW_HEIGHT = 20;

        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private static TimelineLayerWindow _instance = null;
        public static TimelineLayerWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineLayerWindow();
                }
                return _instance;
            }
        }

        private TimelineLayerWindow()
        {
        }

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineLayerPosX;
            y = config.timelineLayerPosY;
            width = config.timelineLayerWidth;
            height = config.timelineLayerHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineLayerPosX = x;
            config.timelineLayerPosY = y;
            config.timelineLayerWidth = width;
            config.timelineLayerHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineLayerVisible;
            set => config.timelineLayerVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            DrawBody();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            // (BackgroundWindow と同じ流儀)
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>
        /// 本体の描画。早期 return しても DrawContent 末尾の
        /// ProcessFocus を飛ばさないようメソッドを分けている
        /// </summary>
        private void DrawBody()
        {
            var currentLayer = timelineManager.currentLayer;
            if (timelineManager.timeline == null || currentLayer == null)
            {
                _view.DrawLabel("タイムラインが作成されていません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            var layerInfo = timelineManager.GetLayerInfo(currentLayer.layerType);
            _view.DrawLabel(layerInfo != null ? layerInfo.displayName : currentLayer.layerName,
                -1, ROW_HEIGHT);
            _view.DrawHorizontalLine(Color.gray);

            currentLayer.DrawWindow(_view);
        }
    }
}
