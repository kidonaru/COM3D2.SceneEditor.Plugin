using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// プラグイン専用の確認/通知ダイアログ。
    /// ゲームの SysDlg（NGUI）はプラグインの IMGUI ウィンドウより奥に描画されて
    /// 裏に隠れてしまうため、IMGUI のモーダルウィンドウとして自前で描画する。
    /// 同時に出すダイアログは 1 つなのでシングルトンで足りる
    /// </summary>
    public class DialogPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903363;

        private static readonly int WINDOW_WIDTH = 360;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;

        /// <summary>表示中のメッセージ。null なら閉じている</summary>
        private string _message;

        /// <summary>確認ダイアログの応答。null なら OK のみの通知ダイアログ</summary>
        private Action _onYes;
        private Action _onNo;
        private bool _isConfirm;

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private readonly GUIView _view = new GUIView();

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

        private static DialogPopupWindow _instance = null;
        public static DialogPopupWindow instance
            => _instance ?? (_instance = new DialogPopupWindow());

        public bool isOpen => _message != null;

        /// <summary>OK ボタンだけの通知ダイアログを表示する</summary>
        public static void ShowDialog(string message)
        {
            var window = instance;
            window._message = message;
            window._isConfirm = false;
            window._onYes = null;
            window._onNo = null;
        }

        /// <summary>はい/いいえの確認ダイアログを表示する</summary>
        public static void ShowConfirmDialog(string message, Action onYes, Action onNo = null)
        {
            var window = instance;
            window._message = message;
            window._isConfirm = true;
            window._onYes = onYes;
            window._onNo = onNo;
        }

        /// <summary>
        /// 閉じてから応答を呼ぶ。応答の中で次のダイアログを開き直せるよう、
        /// 状態のクリアを先に済ませる
        /// </summary>
        private void CloseAndInvoke(Action callback)
        {
            _message = null;
            _onYes = null;
            _onNo = null;

            if (callback != null)
            {
                callback();
            }
        }

        public void Init()
        {
        }

        public void Update()
        {
        }

        public void Close()
        {
            _message = null;
            _onYes = null;
            _onNo = null;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        /// <summary>シーンが変わると応答先の状況も変わるため、応答は呼ばず破棄する</summary>
        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Close();
        }

        public void OnGUI()
        {
            if (_message == null)
            {
                return;
            }

            // メッセージの行数に合わせて高さを算出し、画面中央へ配置する。
            // GUIView は要素ごとに margin を加算して currentPos を進めるため、その分も含める
            var messageHeight = GUIView.CalcHeight(
                messageStyle, _message, WINDOW_WIDTH - PADDING * 2);
            var windowHeight = PADDING + messageHeight + GUIView.defaultMargin
                + 10 + GUIView.defaultMargin + BUTTON_HEIGHT + PADDING;
            _windowRect = new Rect(
                (Screen.width - WINDOW_WIDTH) / 2,
                (Screen.height - windowHeight) / 2,
                WINDOW_WIDTH,
                windowHeight);

            // ModalWindow で背後のウィンドウ操作をブロックする
            GUI.ModalWindow(WINDOW_ID, _windowRect, DrawDialog, "", GUIView.gsWin);
            GUI.BringWindowToFront(WINDOW_ID);
        }

        private void DrawDialog(int id)
        {
            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));
            _view.currentPos = new Vector2(PADDING, PADDING);

            var messageHeight = GUIView.CalcHeight(
                messageStyle, _message, _windowRect.width - PADDING * 2);
            _view.DrawLabel(_message, _windowRect.width - PADDING * 2, messageHeight,
                style: messageStyle);

            _view.AddSpace(10);

            _view.BeginHorizontal();
            {
                if (_isConfirm)
                {
                    // はい/いいえを中央へ寄せる
                    _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH * 2 - 10) / 2;

                    var onYes = _onYes;
                    if (_view.DrawButton("はい", BUTTON_WIDTH, BUTTON_HEIGHT))
                    {
                        CloseAndInvoke(onYes);
                    }

                    var onNo = _onNo;
                    if (_view.DrawButton("いいえ", BUTTON_WIDTH, BUTTON_HEIGHT))
                    {
                        CloseAndInvoke(onNo);
                    }
                }
                else
                {
                    _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH) / 2;

                    if (_view.DrawButton("OK", BUTTON_WIDTH, BUTTON_HEIGHT))
                    {
                        CloseAndInvoke(null);
                    }
                }
            }
            _view.EndLayout();
        }
    }
}
