using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット（メイドの配置・ポーズ・表情とカメラ）の保存/適用ウィンドウ。
    /// 保存時にサムネも撮影し、タイルビューから選んで適用する
    /// </summary>
    public class PresetWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903362;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "シーンプリセット";

        private static readonly int ROW_HEIGHT = 20;

        // タイルの表示サイズ。サムネの縦横比 + 名前ラベル分の高さ
        private static readonly float TILE_WIDTH = 120;
        private static readonly float TILE_HEIGHT =
            TILE_WIDTH * ScenePresetManager.THUM_HEIGHT / ScenePresetManager.THUM_WIDTH + 20;

        // 読み込み中オーバーレイの色。下の内容が透けて見える程度に暗くする
        private static readonly Color OVERLAY_COLOR = new Color(0f, 0f, 0f, 0.7f);

        private readonly GUIView _view = new GUIView();

        private static GUIStyle _gsOverlayLabel = null;

        /// <summary>オーバーレイ中央のラベル。GUIStyle は OnGUI 中でないと作れないため遅延生成する</summary>
        private static GUIStyle gsOverlayLabel
        {
            get
            {
                if (_gsOverlayLabel == null)
                {
                    _gsOverlayLabel = new GUIStyle(GUIView.gsLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                    };
                    // 暗いオーバーレイ上でも読めるよう文字色を固定する
                    _gsOverlayLabel.normal.textColor = Color.white;
                }
                return _gsOverlayLabel;
            }
        }

        private static PresetWindow _instance = null;
        public static PresetWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PresetWindow();
                }
                return _instance;
            }
        }

        private PresetWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.presetPosX;
            y = config.presetPosY;
            width = config.presetWidth;
            height = config.presetHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.presetPosX = x;
            config.presetPosY = y;
            config.presetWidth = width;
            config.presetHeight = height;
        }

        public override bool savedVisible
        {
            get => config.presetVisible;
            set => config.presetVisible = value;
        }

        protected override void DrawContent()
        {
            var localContentRect = ToLocalRect(contentRect);
            _view.Init(localContentRect);

            // 適用の途中で別プリセットを選ばれると保留適用が打ち切られるため、
            // 読み込み中は全操作を無効化してオーバーレイで覆う
            var isLoading = ScenePresetManager.isLoading;
            _view.SetEnabled(!isLoading);

            var currentDirItem = ScenePresetManager.GetOrLoadCurrentDirItem();

            // GUI.enabled はグローバル状態のため、途中で抜けても必ず戻す
            try
            {
                DrawToolRow(currentDirItem);

                _view.DrawHorizontalLine(Color.gray);
                _view.AddSpace(5);

                DrawPresetTiles(currentDirItem);
            }
            finally
            {
                _view.SetEnabled(true);
            }

            if (isLoading)
            {
                DrawLoadingOverlay(localContentRect);
            }
        }

        /// <summary>読み込み中に内容領域を覆うオーバーレイ</summary>
        private void DrawLoadingOverlay(Rect rect)
        {
            var savedColor = GUI.color;
            GUI.color = OVERLAY_COLOR;
            GUI.DrawTexture(rect, GUIView.texWhite);
            GUI.color = savedColor;

            GUI.Label(rect, "読み込み中...", gsOverlayLabel);
        }

        /// <summary>上位フォルダへ戻る / 保存 / フォルダを開く / 一覧更新 + 表示中フォルダ名</summary>
        private void DrawToolRow(ScenePresetItem currentDirItem)
        {
            _view.BeginHorizontal();
            {
                // ルートでは戻り先が無いため無効化する
                if (_view.DrawButton("<", 20, ROW_HEIGHT, currentDirItem.parent != null))
                {
                    ScenePresetManager.currentDirItem = currentDirItem.parent as ScenePresetItem;
                }

                // SceneCapture 仮想フォルダは読み込み専用のため保存させない
                if (_view.DrawButton("保存", 50, ROW_HEIGHT, !currentDirItem.isReadonlyDir))
                {
                    SavePresetWithConfirm();
                }

                // 保存先と揃えるため、開くのは表示中のフォルダ
                if (_view.DrawButton("開く", 50, ROW_HEIGHT))
                {
                    MTEUtils.OpenDirectory(ScenePresetManager.currentDirPath);
                }

                if (_view.DrawButton("更新", 50, ROW_HEIGHT))
                {
                    ScenePresetManager.Reload();
                }

                _view.DrawLabel(currentDirItem.name, -1, ROW_HEIGHT);
            }
            _view.EndLayout();
        }

        /// <summary>タイル一覧。フォルダはクリックで移動、プリセットはクリックで適用、x で削除する</summary>
        private void DrawPresetTiles(ScenePresetItem currentDirItem)
        {
            if (currentDirItem.children == null || currentDirItem.children.Count == 0)
            {
                _view.DrawLabel("保存されたプリセットはありません", -1, ROW_HEIGHT);
                return;
            }

            ScenePresetItem selectedItem = null;
            ScenePresetItem mouseOverItem = null;

            // 下部にマウスオーバー中の名前表示行を確保し、残りをタイルビューへ充てる
            var tileViewHeight = _view.viewRect.height - _view.currentPos.y
                - ROW_HEIGHT - GUIView.defaultMargin;

            _view.DrawTileView(
                currentDirItem,
                -1,
                tileViewHeight,
                TILE_WIDTH,
                TILE_HEIGHT,
                item =>
                {
                    selectedItem = item as ScenePresetItem;
                },
                item =>
                {
                    mouseOverItem = item as ScenePresetItem;
                },
                item =>
                {
                    DeletePresetWithConfirm(item as ScenePresetItem);
                });

            if (selectedItem != null)
            {
                if (selectedItem.isDir)
                {
                    ScenePresetManager.currentDirItem = selectedItem;
                }
                else
                {
                    ScenePresetManager.LoadPreset(selectedItem);
                }
            }

            _view.DrawBox(-1, ROW_HEIGHT);

            if (mouseOverItem != null)
            {
                _view.DrawLabel(mouseOverItem.name, -1, ROW_HEIGHT);
            }
        }

        /// <summary>保存ポップアップ（名前入力 + 対象選択）→ 同名なら上書き確認 → 保存</summary>
        private void SavePresetWithConfirm()
        {
            SavePresetPopupWindow.Show((presetName, options) =>
            {
                if (ScenePresetManager.Exists(presetName))
                {
                    DialogPopupWindow.ShowConfirmDialog(
                        "「" + presetName + "」は既に存在します。上書きしますか？",
                        () => ScenePresetManager.SavePreset(presetName, options));
                    return;
                }

                ScenePresetManager.SavePreset(presetName, options);
            });
        }

        private void DeletePresetWithConfirm(ScenePresetItem item)
        {
            if (item == null)
            {
                return;
            }

            DialogPopupWindow.ShowConfirmDialog(
                "「" + item.name + "」を削除しますか？",
                () => ScenePresetManager.DeletePreset(item));
        }
    }
}
