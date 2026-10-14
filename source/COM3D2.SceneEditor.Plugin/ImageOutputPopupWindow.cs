using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 連番画像出力の開始確認。
    /// 編集値は「開始」を押したときだけタイムラインへ書き戻す (キャンセルでは保存しない)
    /// </summary>
    public class ImageOutputPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903392;
        private static readonly int OVERLAY_WINDOW_ID = 8903387;

        /// <summary>表示中に背後を覆う黒フェードの色 (DialogPopupWindow と同じ)</summary>
        private static readonly Color OVERLAY_COLOR = new Color(0f, 0f, 0f, 0.5f);

        private static readonly int WINDOW_WIDTH = 360;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;
        private static readonly int BUTTON_SPACING = 10;

        /// <summary>開始時の応答。null なら閉じている</summary>
        private Action _onStart;
        private MTEP.TimelineData _timeline;
        private string _message = "";

        private int _scale;
        private GameViewAspectMode _aspectMode;
        private Vector2 _customSize;

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        // 既定の padding のままだと描画だけが 10px ずれ、下端の余白が詰まりボタンも中央から外れる
        private readonly GUIView _view = new GUIView { padding = Vector2.zero };

        private static GUIStyle _messageStyle;

        /// <summary>メッセージ用の中央揃えスタイル。GUIStyle は OnGUI 中でしか作れないため遅延生成する</summary>
        private static GUIStyle messageStyle
        {
            get
            {
                if (_messageStyle == null)
                {
                    _messageStyle = new GUIStyle(GUIView.gsLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                    };
                }
                return _messageStyle;
            }
        }

        private static ImageOutputPopupWindow _instance = null;
        public static ImageOutputPopupWindow instance
            => _instance ?? (_instance = new ImageOutputPopupWindow());

        public bool isOpen => _onStart != null;

        public static void Show(MTEP.TimelineData timeline, string message, Action onStart)
        {
            var window = instance;
            window._timeline = timeline;
            window._message = message;
            window._onStart = onStart;
            window._scale = timeline.imageOutputScale;
            window._aspectMode = timeline.imageOutputAspectMode;
            window._customSize = timeline.imageOutputSize;
        }

        private void ConfirmStart()
        {
            var timeline = _timeline;
            var onStart = _onStart;
            Close();

            // OnGUI の判定の後に差し替わっても、別のタイムラインへ書き戻さない
            if (timeline == null || timeline != MTEP.TimelineManager.instance.timeline)
            {
                return;
            }

            timeline.imageOutputScale = _scale;
            timeline.imageOutputAspectMode = _aspectMode;
            timeline.imageOutputSize = _customSize;
            onStart();
        }

        public void Init()
        {
        }

        public void Update()
        {
        }

        public void Close()
        {
            _onStart = null;
            _timeline = null;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        /// <summary>シーンが変わると出力対象の状況も変わるため、応答は呼ばず破棄する</summary>
        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Close();
        }

        public void OnGUI()
        {
            if (_onStart == null)
            {
                return;
            }

            // 開始まで待つと、押しても何も起きない理由が伝わらない (Undo の全再構築でも差し替わる)
            if (_timeline != MTEP.TimelineManager.instance.timeline)
            {
                Close();
                MTEUtils.ShowDialog("タイムラインが変更されたため連番画像出力を取り消しました");
                return;
            }

            var contentWidth = WINDOW_WIDTH - PADDING * 2;
            var messageHeight = GUIView.CalcHeight(messageStyle, _message, contentWidth);
            // 要素ごとに margin が 1 回入る (AddSpace も 1 要素)
            var windowHeight = PADDING
                + messageHeight + GUIView.defaultMargin
                + BUTTON_SPACING + GUIView.defaultMargin
                + ImageOutputSizeDrawer.GetHeight(_aspectMode)
                + BUTTON_SPACING + GUIView.defaultMargin
                + BUTTON_HEIGHT + PADDING;
            _windowRect = GUIScale.CenterOnScreen(WINDOW_WIDTH, windowHeight);

            // 通常の GUI.DrawTexture は全ウィンドウの奥に描かれるため、全画面の GUI.Window として前面へ出す。
            // 前面化の順序（フェード → ポップアップ）を入れ替えるとフェードがポップアップを覆う
            var screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.Window(OVERLAY_WINDOW_ID, screenRect, DrawOverlay, "", GUIStyle.none);
            GUI.BringWindowToFront(OVERLAY_WINDOW_ID);

            // ModalWindow で背後のウィンドウ操作をブロックする
            GUIScale.ModalWindow(WINDOW_ID, _windowRect, DrawWindow, "", GUIView.gsWin);
            GUI.BringWindowToFront(WINDOW_ID);
        }

        private void DrawOverlay(int id)
        {
            var defaultColor = GUI.color;
            GUI.color = OVERLAY_COLOR;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), GUIView.texWhite);
            GUI.color = defaultColor;
        }

        private void DrawWindow(int id)
        {
            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));
            _view.currentPos = new Vector2(PADDING, PADDING);

            var contentWidth = _windowRect.width - PADDING * 2;
            var messageHeight = GUIView.CalcHeight(messageStyle, _message, contentWidth);
            _view.DrawLabel(_message, contentWidth, messageHeight, style: messageStyle);

            _view.AddSpace(BUTTON_SPACING);

            ImageOutputSizeDrawer.Draw(_view, ref _scale, ref _aspectMode, ref _customSize);

            _view.AddSpace(BUTTON_SPACING);

            _view.BeginHorizontal();
            {
                _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH * 2 - 10) / 2;

                if (_view.DrawButton("開始", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    ConfirmStart();
                }

                if (_view.DrawButton("キャンセル", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    Close();
                }
            }
            _view.EndLayout();
        }
    }
}
