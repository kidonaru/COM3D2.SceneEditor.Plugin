using UnityEngine;
using UnityEngine.SceneManagement;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイアウト保存時に名前を入力させるモーダル。
    /// SavePresetPopupWindow と同じ IMGUI モーダル方式
    /// </summary>
    public class SaveLayoutPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903365;

        private static readonly int WINDOW_WIDTH = 300;
        private static readonly int ROW_HEIGHT = 20;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;
        /// <summary>ボタン行前の余白。高さ算出と AddSpace の両方で使う</summary>
        private static readonly int BUTTON_SPACING = 10;
        /// <summary>保存・キャンセルボタン間の水平の隙間</summary>
        private static readonly int BUTTON_GAP = 10;

        private bool _isOpen;
        private string _layoutName = "";
        /// <summary>検証エラー。null なら未発生。入力が変わったらクリアする</summary>
        private string _errorMessage;
        /// <summary>
        /// 入力中の名前が既存レイアウトと重なるか。
        /// OnGUI は 1 フレームに複数回走るため、入力が変わったときだけ判定してキャッシュする
        /// </summary>
        private bool _isOverwrite;

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private readonly GUIView _view = new GUIView();

        private static SaveLayoutPopupWindow _instance = null;
        public static SaveLayoutPopupWindow instance
            => _instance ?? (_instance = new SaveLayoutPopupWindow());

        /// <summary>名前入力ポップアップを表示する</summary>
        public static void Show()
        {
            var window = instance;
            window._isOpen = true;
            window._layoutName = "";
            window._errorMessage = null;
            window._isOverwrite = false;
        }

        private void ConfirmSave()
        {
            var error = WindowLayoutManager.ValidateLayoutName(_layoutName);
            if (error != null)
            {
                _errorMessage = error;
                return;
            }

            var layoutName = _layoutName;
            Close();
            WindowLayoutManager.SaveLayout(layoutName);
        }

        public void Init()
        {
        }

        public void Update()
        {
        }

        public void Close()
        {
            _isOpen = false;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Close();
        }

        public void OnGUI()
        {
            if (!_isOpen)
            {
                return;
            }

            // 行数 (タイトル + 入力 + 注記) に合わせて高さを算出する
            var rowCount = 3;
            var windowHeight = PADDING
                + (ROW_HEIGHT + GUIView.defaultMargin) * rowCount
                + BUTTON_SPACING + GUIView.defaultMargin + BUTTON_HEIGHT + PADDING;
            _windowRect = new Rect(
                (Screen.width - WINDOW_WIDTH) / 2,
                (Screen.height - windowHeight) / 2,
                WINDOW_WIDTH,
                windowHeight);

            // ModalWindow で背後のウィンドウ操作をブロックする
            GUI.ModalWindow(WINDOW_ID, _windowRect, DrawWindow, "", GUIView.gsWin);
            GUI.BringWindowToFront(WINDOW_ID);
        }

        private void DrawWindow(int id)
        {
            // Enter で保存を確定する (テキストフィールドは Return を消費しないため届く)
            var e = Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                ConfirmSave();
                return;
            }

            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));
            _view.currentPos = new Vector2(PADDING, PADDING);

            var contentWidth = _windowRect.width - PADDING * 2;

            _view.DrawLabel("レイアウト名を入力してください", contentWidth, ROW_HEIGHT);

            _view.DrawTextField(_layoutName, contentWidth, ROW_HEIGHT, value =>
            {
                _layoutName = value;
                _errorMessage = null;
                _isOverwrite = WindowLayoutManager.Exists(value);
            });

            // 注記行。エラーを優先し、無ければ上書き警告を出す
            var note = _errorMessage;
            if (note == null && _isOverwrite)
            {
                note = "同名のレイアウトを上書きします";
            }
            _view.DrawLabel(note ?? "", contentWidth, ROW_HEIGHT);

            _view.AddSpace(BUTTON_SPACING);

            _view.BeginHorizontal();
            {
                _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH * 2 - BUTTON_GAP) / 2;

                if (_view.DrawButton("保存", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    ConfirmSave();
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
