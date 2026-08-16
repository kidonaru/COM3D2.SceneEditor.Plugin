using System;
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

        // トグル 1 個分の余白。CalcWidth はチェックボックス分も含むため、
        // 端数で末尾が欠けない程度の小さな下駄だけ履かせる
        private static readonly float TOGGLE_EXTRA_WIDTH = 8;

        /// <summary>読込トグル同士の間隔。既定 (5) より詰めて 1 行に多く並べる</summary>
        private static readonly float TOGGLE_MARGIN = 2;

        /// <summary>読込トグル行の先頭ラベル幅</summary>
        private static readonly float LOAD_LABEL_WIDTH = 40;

        // タイルの表示サイズ。サムネの縦横比 + 名前ラベル分の高さ。
        // 幅は PNG 配置ウィンドウ (PngPlacementWindow.TILE_WIDTH) と揃えている
        private static readonly float TILE_WIDTH = 96;
        private static readonly float TILE_HEIGHT =
            TILE_WIDTH * ScenePresetManager.THUM_HEIGHT / ScenePresetManager.THUM_WIDTH + 20;

        // 読み込み中オーバーレイの色。下の内容が透けて見える程度に暗くする
        private static readonly Color OVERLAY_COLOR = new Color(0f, 0f, 0f, 0.7f);

        private readonly GUIView _view = new GUIView();

        private static GUIStyle _gsOverlayLabel = null;
        private static GUIStyle _gsRowButton = null;

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

        /// <summary>リスト表示 1 行のボタン。名前の頭を揃えたいので左寄せにする</summary>
        private static GUIStyle gsRowButton
        {
            get
            {
                if (_gsRowButton == null)
                {
                    _gsRowButton = new GUIStyle(GUIView.gsButton)
                    {
                        alignment = TextAnchor.MiddleLeft,
                    };
                    _gsRowButton.padding.left = 6;
                }
                return _gsRowButton;
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
                DrawLoadOptionRow();

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

        /// <summary>
        /// ロード時に反映するカテゴリのトグル行。
        /// 固定 3 カテゴリ（カメラ・メイド・背景）に続けて、
        /// 発見済みの外部プロバイダを短縮表示名で並べる
        /// </summary>
        private void DrawLoadOptionRow()
        {
            // margin はビュー全体の状態のため、この行を抜けたら必ず戻す
            var savedMargin = _view.margin;
            _view.margin = TOGGLE_MARGIN;

            _view.BeginHorizontal();
            {
                _view.DrawLabel("読込:", LOAD_LABEL_WIDTH, ROW_HEIGHT);

                DrawLoadToggle("カメラ", ScenePresetManager.loadCamera,
                    value => ScenePresetManager.loadCamera = value);
                DrawLoadToggle("メイド", ScenePresetManager.loadMaids,
                    value => ScenePresetManager.loadMaids = value);
                // 背景トグルは背景・ライト・PNG 配置をまとめて制御する
                DrawLoadToggle("背景", ScenePresetManager.loadBackground,
                    value => ScenePresetManager.loadBackground = value);

                foreach (var provider in ScenePresetProviderRegistry.providers)
                {
                    // ラムダのキャプチャ対象をループ変数から切り離す
                    var providerId = provider.id;
                    DrawLoadToggle(
                        provider.shortDisplayName,
                        ScenePresetManager.IsProviderLoadEnabled(providerId),
                        value => ScenePresetManager.SetProviderLoadEnabled(providerId, value));
                }
            }
            _view.EndLayout();

            _view.margin = savedMargin;
        }

        /// <summary>
        /// 読込トグル 1 個。ラベル実寸に合わせた幅で描き、
        /// 行に収まらなくなったら次の行へ折り返す
        /// </summary>
        private void DrawLoadToggle(string label, bool value, Action<bool> onChanged)
        {
            var width = GUIView.CalcWidth(GUIView.gsToggle, label) + TOGGLE_EXTRA_WIDTH;

            // 行頭 (x = 0) では改行しても無意味なので、2 個目以降だけ判定する
            var rowWidth = _view.viewRect.width - _view.padding.x * 2;
            if (_view.currentPos.x > 0 && _view.currentPos.x + width > rowWidth)
            {
                _view.EndLayout();
                _view.BeginHorizontal();
            }

            _view.DrawToggle(label, value, width, ROW_HEIGHT, newValue =>
            {
                onChanged(newValue);
                config.dirty = true;
            });
        }

        /// <summary>タイル一覧。フォルダはクリックで移動、プリセットはクリックで適用、x で削除する</summary>
        private void DrawPresetTiles(ScenePresetItem currentDirItem)
        {
            if (currentDirItem.children == null || currentDirItem.children.Count == 0)
            {
                _view.DrawLabel("保存されたプリセットはありません", -1, ROW_HEIGHT);
                return;
            }

            // SceneCapture プリセットはサムネを持たず、タイルにすると空枠が並ぶだけになる
            if (currentDirItem.isSceneCapture)
            {
                DrawPresetRows(currentDirItem);
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

            OpenItem(selectedItem);

            _view.DrawBox(-1, ROW_HEIGHT);

            if (mouseOverItem != null)
            {
                _view.DrawLabel(mouseOverItem.name, -1, ROW_HEIGHT);
            }
        }

        /// <summary>
        /// リスト一覧。サムネを持たない SceneCapture プリセット用で、
        /// タイルと同じくフォルダはクリックで移動、プリセットはクリックで適用する
        /// （読み込み専用のため削除ボタンは出さない）
        /// </summary>
        private void DrawPresetRows(ScenePresetItem currentDirItem)
        {
            var children = currentDirItem.children;

            // 現在読み込み中の強調 (currentIndex) は使わない。
            // SceneCapture プリセットは選択状態を持たない (ScenePresetManager.UpdateSelection 参照)
            var selectedIndex = _view.DrawListView(
                children,
                // フォルダとプリセットを見分けられるよう、フォルダ名は括弧で囲む
                (child, index) => child.isDir ? "[" + child.name + "]" : child.name,
                null,
                -1,
                -1,
                -1,
                ROW_HEIGHT,
                gsRowButton);

            if (selectedIndex >= 0)
            {
                OpenItem(children[selectedIndex] as ScenePresetItem);
            }
        }

        /// <summary>一覧で選ばれた項目を開く。フォルダなら移動、プリセットなら適用する</summary>
        private static void OpenItem(ScenePresetItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.isDir)
            {
                ScenePresetManager.currentDirItem = item;
            }
            else
            {
                ScenePresetManager.LoadPreset(item);
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
