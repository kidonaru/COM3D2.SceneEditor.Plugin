using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// プラグイン情報を表示するモーダル。
    /// SaveLayoutPopupWindow と同じ IMGUI モーダル方式
    /// </summary>
    public class AboutPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903382;

        /// <summary>ウィンドウ幅の下限。URL が長ければそちらに合わせて広げる</summary>
        private static readonly int MIN_WINDOW_WIDTH = 360;
        private static readonly int ROW_HEIGHT = 20;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;
        /// <summary>アイコンの表示サイズ。元画像は 32x32</summary>
        private static readonly int ICON_SIZE = 48;
        /// <summary>アイコンと名称の間隔</summary>
        private static readonly int ICON_GAP = 12;
        /// <summary>ボタン行前の余白。高さ算出と AddSpace の両方で使う</summary>
        private static readonly int BUTTON_SPACING = 10;

        private bool _isOpen;

        /// <summary>プラグインアイコン。初回描画時に生成する</summary>
        private Texture2D _iconTexture;
        /// <summary>毎フレーム呼ばれるため、失敗も記録して再デコード・再ログを 1 回に抑える</summary>
        private bool _iconFailed;

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        // 各行を PADDING 分内側から始めるため、余白は padding で持たせる
        private readonly GUIView _view = new GUIView
        {
            padding = new Vector2(PADDING, PADDING),
        };

        private static AboutPopupWindow _instance = null;
        public static AboutPopupWindow instance
            => _instance ?? (_instance = new AboutPopupWindow());

        /// <summary>プラグイン情報ポップアップを表示する</summary>
        public static void Show()
        {
            instance._isOpen = true;
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

            // 行数 (名称ブロック + リンク) に合わせて高さを算出する
            var windowHeight = PADDING
                + ICON_SIZE + GUIView.defaultMargin
                + ROW_HEIGHT + GUIView.defaultMargin
                + BUTTON_SPACING + GUIView.defaultMargin + BUTTON_HEIGHT + PADDING;
            // gsLabel は折り返さないため、URL 全文が収まる幅を確保する
            var windowWidth = Mathf.Max(MIN_WINDOW_WIDTH,
                GUIView.CalcWidth(GUIView.gsLabel, PluginInfo.DocumentUrl) + PADDING * 2);

            _windowRect = new Rect(
                (Screen.width - windowWidth) / 2,
                (Screen.height - windowHeight) / 2,
                windowWidth,
                windowHeight);

            // ModalWindow で背後のウィンドウ操作をブロックする
            GUI.ModalWindow(WINDOW_ID, _windowRect, DrawWindow, "", GUIView.gsWin);
            GUI.BringWindowToFront(WINDOW_ID);
        }

        private void DrawWindow(int id)
        {
            // Enter / Esc のどちらでも閉じられるようにする
            var e = Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter
                    || e.keyCode == KeyCode.Escape))
            {
                e.Use();
                Close();
                return;
            }

            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));

            var contentWidth = _windowRect.width - PADDING * 2;

            DrawIconRow(contentWidth);

            // リンクであることが分かるようアクセント色で示す
            _view.DrawLabel(PluginInfo.DocumentUrl, contentWidth, ROW_HEIGHT,
                GUIView.option.accentColor, null,
                () => Application.OpenURL(PluginInfo.DocumentUrl));

            _view.AddSpace(BUTTON_SPACING);

            _view.BeginHorizontal();
            {
                _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH) / 2 - PADDING;

                if (_view.DrawButton("閉じる", BUTTON_WIDTH, BUTTON_HEIGHT))
                {
                    Close();
                }
            }
            _view.EndLayout();
        }

        /// <summary>アイコンの右へ名称とバージョンを縦に積む</summary>
        private void DrawIconRow(float contentWidth)
        {
            var icon = GetIconTexture();

            _view.BeginHorizontal();
            {
                if (icon != null)
                {
                    _view.DrawTexture(icon, ICON_SIZE, ICON_SIZE);
                }
                else
                {
                    // アイコンを読めなくても名称の位置がずれないよう場所だけ確保する
                    _view.AddSpace(ICON_SIZE, ICON_SIZE);
                }

                // アイコン描画で currentPos.x に margin が加算済みなので、差分だけ足して ICON_GAP にする
                var textX = _view.currentPos.x + ICON_GAP - _view.margin;
                var textWidth = contentWidth - textX;
                // 横並び中は行送りされないため、2 行分の位置を自前で指定する。
                // アイコンに対して縦中央へ寄せる
                var textY = _view.currentPos.y + (ICON_SIZE - ROW_HEIGHT * 2) * 0.5f;

                _view.currentPos = new Vector2(textX, textY);
                _view.DrawLabel(PluginInfo.PluginFullName, textWidth, ROW_HEIGHT);

                _view.currentPos = new Vector2(textX, textY + ROW_HEIGHT);
                _view.DrawLabel("Version " + PluginInfo.PluginVersion, textWidth, ROW_HEIGHT);
            }
            _view.EndLayout();
        }

        /// <summary>プラグインアイコンを取得する。読み込めなければ null</summary>
        private Texture2D GetIconTexture()
        {
            if (_iconTexture == null && !_iconFailed)
            {
                _iconTexture = ToolbarIcons.CreateTextureFromPng(
                    PluginInfo.Icon, "プラグインアイコン");
                _iconFailed = _iconTexture == null;
            }
            return _iconTexture;
        }
    }
}
