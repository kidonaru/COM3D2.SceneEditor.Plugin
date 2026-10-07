using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 顔の影の項目 1 件分の行 (トグル・解除ボタン)。表情ウィンドウの影タブと
    /// TimelineItemInspector (表情レイヤーの表情設定の項目表示) で共有する。
    /// トグルには今の値を出し、SE が上書きしている項目は名前に「*」を付ける
    /// </summary>
    public static class FaceShadowRowDrawer
    {
        private const float ClearButtonWidth = 50f;

        private static MaidFaceRendererManager faceRenderer => MaidFaceRendererManager.instance;

        public static void Draw(GUIView view, Maid target, FaceShadowItem item, float rowHeight)
        {
            bool overridden;
            var hasOverride = faceRenderer.TryGetOverride(target, item.name, out overridden);
            var label = hasOverride ? item.displayName + " *" : item.displayName;

            view.BeginHorizontal();
            {
                // トグルは残り幅いっぱい (-1) にするため、解除ボタンを先に置く
                if (view.DrawButton("解除", ClearButtonWidth, rowHeight, hasOverride))
                {
                    RecordEdit(target, item.displayName + " 解除");
                    faceRenderer.ClearOverride(target, item.name);
                }

                view.DrawToggle(label, faceRenderer.IsEnabled(target, item.name), -1, rowHeight,
                    value =>
                    {
                        RecordEdit(target, item.displayName);
                        faceRenderer.SetOverride(target, item.name, value);
                    });
            }
            view.EndLayout();
        }

        public static void RecordEdit(Maid target, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.FaceShadow, "顔の影: " + label);
        }
    }
}
