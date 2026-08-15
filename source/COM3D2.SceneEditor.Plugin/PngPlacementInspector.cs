using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector に PNG 配置固有のパラメータを描く。
    /// 位置・回転・拡縮は Inspector 共通の Transform 行が担うため、その続きに足す
    /// </summary>
    public static class PngPlacementInspector
    {
        private const float LABEL_WIDTH = 70f;
        private const float ROW_HEIGHT = 20f;

        // 表示順の増減量 (< > と << >>)
        private const int RENDER_QUEUE_STEP = 10;
        private const int RENDER_QUEUE_BIG_STEP = 100;

        private static PngPlacementManager pngManager => PngPlacementManager.instance;

        /// <summary>選択中が PNG 配置なら固有パラメータを描く。描いたら true</summary>
        public static bool Draw(GUIView view, GameObject go)
        {
            var data = pngManager.FindByRoot(go);
            if (data == null)
            {
                return false;
            }

            view.DrawHorizontalLine(Color.gray);

            view.BeginHorizontal();
            {
                view.DrawToggle("表示", data.visible, 60, ROW_HEIGHT, value =>
                {
                    RecordPngEdit("表示切替");
                    pngManager.SetVisible(data, value);
                });

                view.DrawToggle("ビルボード", data.billboard, 100, ROW_HEIGHT, value =>
                {
                    RecordPngEdit("ビルボード切替");
                    pngManager.SetBillboard(data, value);
                });
            }
            view.EndLayout();

            // ColorPickerWindow はラベル文字列で編集対象を識別するため、
            // 他ウィンドウの色行とラベルを重複させないこと
            var fieldCache = view.GetColorFieldCache("PNG色", true);
            view.DrawColor(fieldCache, data.color, Color.white, value =>
            {
                RecordPngEdit("色");
                pngManager.SetColor(data, value, data.brightness);
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "明るさ",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = data.brightness,
                onChanged = value =>
                {
                    RecordPngEdit("明るさ");
                    pngManager.SetColor(data, data.color, value);
                },
            });

            view.DrawIntSelect("表示順", RENDER_QUEUE_STEP, RENDER_QUEUE_BIG_STEP,
                () =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, PngPlacementManager.DefaultRenderQueue);
                },
                data.renderQueue,
                value =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, value);
                },
                diff =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, data.renderQueue + diff);
                });

            return true;
        }

        /// <summary>PNG 操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordPngEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.PngPlacement,
                "PNG: " + label);
        }
    }
}
