using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 連番画像出力の倍率・比率・カスタムサイズの入力欄と出力サイズ表示。
    /// タイムライン設定ウィンドウと出力前ポップアップで共有する
    /// </summary>
    public static class ImageOutputSizeDrawer
    {
        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 60;
        private static readonly int SCALE_BUTTON_WIDTH = 40;
        private static readonly int ASPECT_BUTTON_WIDTH = 52;
        private static readonly int ASPECT_ROW_COUNT = 2;
        private static readonly int ASPECT_FIRST_ROW_COUNT = 4;
        private static readonly int INT_FIELD_WIDTH = 80;

        /// <summary>Draw が描く高さ (行数 × (行高 + margin))</summary>
        public static float GetHeight(GameViewAspectMode mode)
        {
            // 倍率・比率・出力サイズの行 + カスタムの幅・高さ
            var rows = 1 + ASPECT_ROW_COUNT + 1 + (mode == GameViewAspectMode.Custom ? 2 : 0);
            return rows * (ROW_HEIGHT + GUIView.defaultMargin);
        }

        /// <summary>現在の画面サイズと GPU 上限で出力した場合の解像度</summary>
        public static void GetOutputSize(
            int scale, GameViewAspectMode mode, Vector2 customSize, out int width, out int height)
        {
            int renderWidth, renderHeight;
            Rect cropRect;
            MTEP.ImageOutputLayout.GetLayout(
                Screen.width, Screen.height, scale, mode, customSize, SystemInfo.maxTextureSize,
                out renderWidth, out renderHeight, out cropRect);
            width = (int)cropRect.width;
            height = (int)cropRect.height;
        }

        public static void Draw(
            GUIView view, ref int scale, ref GameViewAspectMode mode, ref Vector2 customSize)
        {
            var newScale = scale;
            var newMode = mode;
            var newSize = customSize;

            // 行全体を BeginEnabled で囲むと、DrawButton が終わるたびに GUI.enabled を戻すため 2 個目以降が有効に見える
            var scaleEnabled = mode != GameViewAspectMode.Custom;
            view.BeginHorizontal();
            {
                view.DrawLabel("倍率", LABEL_WIDTH, ROW_HEIGHT);
                for (var s = 1; s <= GameViewAspect.MAX_SCALE; s++)
                {
                    var isSelected = scale == s;
                    if (view.DrawButton(s + "x", SCALE_BUTTON_WIDTH, ROW_HEIGHT,
                        scaleEnabled, null, isSelected ? GUIView.gsSelectedButton : null) && !isSelected)
                    {
                        newScale = s;
                    }
                }
            }
            view.EndLayout();

            // コンボボックスにすると、ドロップダウンが別ウィンドウのためモーダルの中から開けない。
            // 1 行に 7 個並べると既定幅 320px のタイムライン設定ウィンドウからはみ出す
            var modes = GameViewAspect.modes;
            for (var row = 0; row < ASPECT_ROW_COUNT; row++)
            {
                var start = row == 0 ? 0 : ASPECT_FIRST_ROW_COUNT;
                var end = row == 0 ? ASPECT_FIRST_ROW_COUNT : modes.Length;
                view.BeginHorizontal();
                {
                    view.DrawLabel(row == 0 ? "比率" : "", LABEL_WIDTH, ROW_HEIGHT);
                    for (var i = start; i < end; i++)
                    {
                        var m = modes[i];
                        var isSelected = mode == m;
                        if (view.DrawButton(GameViewAspect.GetDisplayName(m), ASPECT_BUTTON_WIDTH, ROW_HEIGHT,
                            true, null, isSelected ? GUIView.gsSelectedButton : null) && !isSelected)
                        {
                            newMode = m;
                        }
                    }
                }
                view.EndLayout();
            }

            if (mode == GameViewAspectMode.Custom)
            {
                view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "幅",
                    labelWidth = LABEL_WIDTH,
                    value = Mathf.RoundToInt(customSize.x),
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + INT_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    onChanged = value => newSize.x = value,
                });
                view.DrawIntField(new GUIView.IntFieldOption
                {
                    label = "高さ",
                    labelWidth = LABEL_WIDTH,
                    value = Mathf.RoundToInt(customSize.y),
                    minValue = 1,
                    maxValue = GameViewAspect.MAX_CUSTOM_SIZE,
                    width = LABEL_WIDTH + INT_FIELD_WIDTH,
                    height = ROW_HEIGHT,
                    onChanged = value => newSize.y = value,
                });
            }

            int width, height;
            GetOutputSize(scale, mode, customSize, out width, out height);
            view.BeginHorizontal();
            {
                view.DrawLabel("出力サイズ", LABEL_WIDTH, ROW_HEIGHT);
                view.DrawLabel(width + " x " + height, -1, ROW_HEIGHT);
            }
            view.EndLayout();

            // 描画の途中で mode を書き換えると、呼び出し元が GetHeight で求めた高さと描く行数が食い違う
            scale = newScale;
            mode = newMode;
            customSize = newSize;
        }
    }
}
