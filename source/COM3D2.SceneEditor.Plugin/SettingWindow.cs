using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// プラグイン全体の設定ウィンドウ。
    /// config を直接編集し、変更時に dirty を立てて ConfigManager に保存させる
    /// </summary>
    public class SettingWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903373;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "設定";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;
        private static readonly int SCALE_BUTTON_WIDTH = 40;
        private static readonly int INT_FIELD_WIDTH = 60;
        private static readonly int TAB_WIDTH = 70;
        // 横並びトグルの左側の幅。ラベルが見切れない程度に固定する
        private static readonly int TOGGLE_WIDTH = 130;

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum SettingTabType
        {
            撮影,
            グリッド,
            履歴,
            プリセット,
        }

        private SettingTabType _tabType = SettingTabType.撮影;

        private readonly GUIView _view = new GUIView();

        private static SettingWindow _instance = null;
        public static SettingWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SettingWindow();
                }
                return _instance;
            }
        }

        private SettingWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.settingPosX;
            y = config.settingPosY;
            width = config.settingWidth;
            height = config.settingHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.settingPosX = x;
            config.settingPosY = y;
            config.settingWidth = width;
            config.settingHeight = height;
        }

        public override bool savedVisible
        {
            get => config.settingVisible;
            set => config.settingVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            // タブはスクロールビューの外に置き、どこまでスクロールしても切り替えられるようにする
            _tabType = _view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」になるため、
            // MaidWindowBase.DrawInnerTabs と同じく通常の行間に合わせて詰める
            _view.currentPos.y -= 5 + GUIView.defaultMargin;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            switch (_tabType)
            {
                case SettingTabType.撮影:
                    DrawScreenshotSection();
                    break;
                case SettingTabType.グリッド:
                    DrawGridSection();
                    break;
                case SettingTabType.履歴:
                    DrawHistorySection();
                    break;
                case SettingTabType.プリセット:
                    DrawPresetSection();
                    break;
            }

            _view.EndScrollView();
        }

        /// <summary>スクリーンショットの解像度倍率と撮影ボタン</summary>
        private void DrawScreenshotSection()
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("倍率", LABEL_WIDTH, ROW_HEIGHT);

                for (var scale = 1; scale <= ScreenshotManager.MAX_SCALE; scale++)
                {
                    var isSelected = config.screenshotScale == scale;
                    if (_view.DrawButton(
                        scale + "x", SCALE_BUTTON_WIDTH, ROW_HEIGHT,
                        true, null,
                        isSelected ? GUIView.gsSelectedButton : null) && !isSelected)
                    {
                        config.screenshotScale = scale;
                        config.dirty = true;
                    }
                }
            }
            _view.EndLayout();

            int width, height;
            ScreenshotManager.GetCaptureSize(out width, out height);
            _view.BeginHorizontal();
            {
                _view.DrawLabel("出力サイズ", LABEL_WIDTH, ROW_HEIGHT);
                _view.DrawLabel(width + " x " + height, -1, ROW_HEIGHT);
            }
            _view.EndLayout();

            // 成否はメニューバーの撮影項目と同じくログにだけ出す
            // (保存先パスも含めて MTEUtils のログに残るため、UI 側では拾わない)
            if (_view.DrawButton("撮影", 100, ROW_HEIGHT))
            {
                ScreenshotManager.Capture();
            }
        }

        /// <summary>グリッド表示の詳細設定。反映は GridRenderer が毎フレーム config を見るだけで済む</summary>
        private void DrawGridSection()
        {
            _view.BeginHorizontal();
            {
                DrawGridToggle("グリッドを表示", config.isGridVisible,
                    value => config.isGridVisible = value, TOGGLE_WIDTH);
                DrawGridToggle("編集中のみ表示", config.isGridVisibleOnlyEdit,
                    value => config.isGridVisibleOnlyEdit = value);
            }
            _view.EndLayout();

            _view.AddSpace(5);
            _view.DrawLabel("ワールドグリッド", -1, ROW_HEIGHT);

            _view.BeginHorizontal();
            {
                DrawGridToggle("床グリッドを表示", config.isGridVisibleInWorld,
                    value => config.isGridVisibleInWorld = value, TOGGLE_WIDTH);
                DrawGridToggle("XYZ軸線を表示", config.isGridAxisVisible,
                    value => config.isGridAxisVisible = value);
            }
            _view.EndLayout();

            DrawGridSlider("マス数", config.gridCountInWorld, 2f, GridRenderer.MaxGridCount, 1f,
                Config.DefaultGridCountInWorld,
                value => config.gridCountInWorld = Mathf.RoundToInt(value));
            DrawGridSlider("マスの大きさ", config.gridCellSize, 0.05f, 5f, 0.05f,
                Config.DefaultGridCellSize,
                value => config.gridCellSize = value);
            DrawGridSlider("不透明度", config.gridAlphaInWorld, 0f, 1f, 0.01f,
                Config.DefaultGridAlphaInWorld,
                value => config.gridAlphaInWorld = value);
            DrawGridSlider("線の幅", config.gridLineWidthInWorld, GridRenderer.MinLineWidth, GridRenderer.MaxLineWidth, 0.1f,
                Config.DefaultGridLineWidthInWorld,
                value => config.gridLineWidthInWorld = value);
            DrawGridColor("床グリッド色", config.gridColorInWorld, value => config.gridColorInWorld = value);

            _view.AddSpace(5);
            _view.DrawLabel("画面分割グリッド", -1, ROW_HEIGHT);

            DrawGridToggle("分割線を表示", config.isGridVisibleInDisplay,
                value => config.isGridVisibleInDisplay = value);
            DrawGridSlider("分割数", config.gridCountInDisplay, 2f, GridRenderer.MaxDisplayGridCount, 1f,
                Config.DefaultGridCountInDisplay,
                value => config.gridCountInDisplay = Mathf.RoundToInt(value));
            DrawGridSlider("不透明度", config.gridAlphaInDisplay, 0f, 1f, 0.01f,
                Config.DefaultGridAlphaInDisplay,
                value => config.gridAlphaInDisplay = value);
            DrawGridSlider("線の幅", config.gridLineWidthInDisplay, GridRenderer.MinLineWidth, GridRenderer.MaxLineWidth, 0.1f,
                Config.DefaultGridLineWidthInDisplay,
                value => config.gridLineWidthInDisplay = value);
            DrawGridColor("分割線の色", config.gridColorInDisplay, value => config.gridColorInDisplay = value);

            _view.DrawLabel("ゲーム画面にのみ表示", -1, ROW_HEIGHT, textColor: Color.gray);
        }

        private void DrawGridToggle(string label, bool value, Action<bool> onChanged, float width = -1)
        {
            _view.DrawToggle(label, value, width, ROW_HEIGHT, newValue =>
            {
                onChanged(newValue);
                config.dirty = true;
            });
        }

        private void DrawGridSlider(
            string label, float value, float min, float max, float step, float defaultValue,
            Action<float> onChanged)
        {
            _view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = min,
                max = max,
                step = step,
                defaultValue = defaultValue,
                value = value,
                onChanged = newValue =>
                {
                    onChanged(newValue);
                    config.dirty = true;
                },
            });
        }

        private void DrawGridColor(string label, Color color, Action<Color> onChanged)
        {
            // ラベルは ColorPickerWindow の編集対象キーにもなるため、重複しない名前を渡すこと
            var fieldCache = _view.GetColorFieldCache(label, false);
            _view.DrawColor(fieldCache, color, Color.white, newValue =>
            {
                onChanged(newValue);
                config.dirty = true;
            });
        }

        /// <summary>操作履歴の保持数。0 で履歴機能自体を無効化する</summary>
        private void DrawHistorySection()
        {
            _view.DrawIntField(new GUIView.IntFieldOption
            {
                label = "最大保持数",
                labelWidth = LABEL_WIDTH,
                value = config.historyLimit,
                minValue = 0,
                maxValue = 999,
                width = LABEL_WIDTH + INT_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value =>
                {
                    config.historyLimit = value;
                    config.dirty = true;
                },
            });

            // 減らした分の切り詰めは HistoryManager の記録時に走るため、
            // 既存の履歴は次の操作を記録したときに新しい上限まで縮む
            _view.DrawLabel("0 で履歴を無効化", -1, ROW_HEIGHT, textColor: Color.gray);
        }

        /// <summary>シーンプリセットの自動ロード設定。対象の指定はプリセット一覧のホームアイコンで行う</summary>
        private void DrawPresetSection()
        {
            _view.DrawLabel("SceneDaily(事務所) の自動ロード", -1, ROW_HEIGHT);

            _view.BeginHorizontal();
            {
                _view.DrawLabel("対象", LABEL_WIDTH, ROW_HEIGHT);

                var hasTarget = ScenePresetManager.hasAutoLoadTarget;
                _view.DrawLabel(
                    hasTarget ? ScenePresetManager.autoLoadName : "未設定",
                    -1, ROW_HEIGHT,
                    textColor: hasTarget ? Color.white : Color.gray);
            }
            _view.EndLayout();

            if (_view.DrawButton("解除", 50, ROW_HEIGHT, ScenePresetManager.hasAutoLoadTarget))
            {
                ScenePresetManager.ClearAutoLoadTarget();
            }

            _view.DrawToggle("セッション中 1 回のみ", config.scenePresetAutoLoadOnceOnly,
                -1, ROW_HEIGHT, newValue =>
                {
                    config.scenePresetAutoLoadOnceOnly = newValue;
                    config.dirty = true;
                });

            _view.DrawLabel("対象はプリセット一覧の家アイコンで指定", -1, ROW_HEIGHT,
                textColor: Color.gray);
        }
    }
}
