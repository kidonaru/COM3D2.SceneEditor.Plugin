using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>シーンプリセット保存時の対象カテゴリ選択</summary>
    public class ScenePresetSaveOptions
    {
        public bool saveCamera = true;
        public bool saveMaids = true;
        /// <summary>保存対象の外部プロバイダ id</summary>
        public List<string> enabledProviderIds = new List<string>();
    }

    /// <summary>
    /// プリセット保存時に保存対象カテゴリを選択させるモーダル。
    /// DialogPopupWindow と同じ IMGUI モーダル方式。チェック状態は Config に永続化し、
    /// 次回のデフォルトにする
    /// </summary>
    public class SavePresetPopupWindow : IGUIWindow
    {
        public static readonly int WINDOW_ID = 8903364;

        private static readonly int WINDOW_WIDTH = 300;
        private static readonly int ROW_HEIGHT = 20;
        private static readonly int BUTTON_WIDTH = 80;
        private static readonly int BUTTON_HEIGHT = 24;
        private static readonly int PADDING = 15;
        /// <summary>ボタン行前の余白。高さ算出と AddSpace の両方で使う</summary>
        private static readonly int BUTTON_SPACING = 10;

        /// <summary>保存確定時の応答。null なら閉じている</summary>
        private Action<string, ScenePresetSaveOptions> _onSave;

        /// <summary>プリセット名の入力値。表示のたびに読み込み中プリセット名で初期化する</summary>
        private string _presetName = "";
        /// <summary>名前検証エラー。null なら非表示</summary>
        private string _errorMessage;

        private bool _saveCamera;
        private bool _saveMaids;
        /// <summary>プロバイダごとのチェック状態 (provider.id → チェック有無)</summary>
        private readonly Dictionary<string, bool> _providerChecks = new Dictionary<string, bool>();

        private Rect _windowRect;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private readonly GUIView _view = new GUIView();

        private static Config config => ConfigManager.instance.config;

        private static SavePresetPopupWindow _instance = null;
        public static SavePresetPopupWindow instance
            => _instance ?? (_instance = new SavePresetPopupWindow());

        public bool isOpen => _onSave != null;

        /// <summary>保存対象選択ポップアップを表示する。前回のチェック状態を初期値にする</summary>
        public static void Show(Action<string, ScenePresetSaveOptions> onSave)
        {
            // 初回走査後にロードされたプラグインも拾えるよう、表示のたびに再走査する
            ScenePresetProviderRegistry.Refresh();

            var window = instance;
            window._onSave = onSave;
            window._errorMessage = null;
            // 上書き保存が主な操作なので、読み込み中のプリセット名を既定にする
            window._presetName = ScenePresetManager.currentPresetName;
            window._saveCamera = config.scenePresetSaveCamera;
            window._saveMaids = config.scenePresetSaveMaids;

            var disabledIds = new HashSet<string>(
                (config.scenePresetDisabledProviders ?? "").Split(','));
            window._providerChecks.Clear();
            foreach (var provider in ScenePresetProviderRegistry.providers)
            {
                window._providerChecks[provider.id] = !disabledIds.Contains(provider.id);
            }
        }

        /// <summary>名前を検証し、チェック状態を Config に書き戻して保存対象を組み立てて応答する</summary>
        private void ConfirmSave()
        {
            _errorMessage = ScenePresetManager.ValidatePresetName(_presetName);
            if (_errorMessage != null)
            {
                return;
            }

            config.scenePresetSaveCamera = _saveCamera;
            config.scenePresetSaveMaids = _saveMaids;
            config.scenePresetDisabledProviders = string.Join(",",
                _providerChecks.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray());
            config.dirty = true;

            var options = new ScenePresetSaveOptions
            {
                saveCamera = _saveCamera,
                saveMaids = _saveMaids,
                enabledProviderIds = _providerChecks
                    .Where(pair => pair.Value).Select(pair => pair.Key).ToList(),
            };

            var onSave = _onSave;
            var presetName = _presetName;
            Close();
            onSave(presetName, options);
        }

        public void Init()
        {
        }

        public void Update()
        {
        }

        public void Close()
        {
            _onSave = null;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        /// <summary>シーンが変わると保存対象の状況も変わるため、応答は呼ばず破棄する</summary>
        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Close();
        }

        public void OnGUI()
        {
            if (_onSave == null)
            {
                return;
            }

            // 行数（名前入力 + タイトル + 固定 2 カテゴリ + プロバイダ数 + エラー表示）に合わせて高さを算出する
            var rowCount = 2 + 2 + _providerChecks.Count + (_errorMessage != null ? 1 : 0);
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
            _view.Init(new Rect(0f, 0f, _windowRect.width, _windowRect.height));
            _view.currentPos = new Vector2(PADDING, PADDING);

            var contentWidth = _windowRect.width - PADDING * 2;

            _view.BeginHorizontal();
            {
                _view.DrawLabel("名前", 40, ROW_HEIGHT);
                _view.DrawTextField(new GUIView.TextFieldOption
                {
                    value = _presetName,
                    width = contentWidth - 40 - GUIView.defaultMargin,
                    hiddenButton = true,
                    // 入力し直したら前回の検証エラー表示を消す
                    onChanged = value =>
                    {
                        _presetName = value;
                        _errorMessage = null;
                    },
                });
            }
            _view.EndLayout();

            if (_errorMessage != null)
            {
                _view.DrawLabel(_errorMessage, contentWidth, ROW_HEIGHT, Color.red);
            }

            _view.DrawLabel("保存する項目を選択してください", contentWidth, ROW_HEIGHT);

            _view.DrawToggle("カメラ", _saveCamera, contentWidth, ROW_HEIGHT,
                value => _saveCamera = value);
            _view.DrawToggle("メイド (位置・ポーズ・表情)", _saveMaids, contentWidth, ROW_HEIGHT,
                value => _saveMaids = value);

            foreach (var provider in ScenePresetProviderRegistry.providers)
            {
                var providerId = provider.id;
                bool isChecked;
                if (!_providerChecks.TryGetValue(providerId, out isChecked))
                {
                    continue;
                }
                _view.DrawToggle(provider.displayName, isChecked, contentWidth, ROW_HEIGHT,
                    value => _providerChecks[providerId] = value);
            }

            _view.AddSpace(BUTTON_SPACING);

            _view.BeginHorizontal();
            {
                _view.currentPos.x = (_windowRect.width - BUTTON_WIDTH * 2 - 10) / 2;

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
