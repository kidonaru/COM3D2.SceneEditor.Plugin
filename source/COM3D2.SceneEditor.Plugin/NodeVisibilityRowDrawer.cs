using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ノード 1 件分の行 (表示トグル・解除ボタン)。脱衣ウィンドウのノード表示タブと
    /// TimelineItemInspector (ノード表示レイヤーの項目表示) で共有する。
    /// トグルには実際の表示を出し、SE が上書きしているノードは名前に「*」を付ける
    /// </summary>
    public static class NodeVisibilityRowDrawer
    {
        private const float ClearButtonWidth = 50f;

        public static void Draw(GUIView view, Maid target, MaidNodeVisibilityNode node,
            float rowHeight, float indentWidth)
        {
            bool overridden;
            var hasOverride = MaidNodeVisibilityController.TryGetOverride(target, node.boneName, out overridden);
            var label = hasOverride ? node.displayName + " *" : node.displayName;

            view.BeginHorizontal();
            {
                if (indentWidth > 0f)
                {
                    view.AddSpace(indentWidth, rowHeight);
                }

                // トグルは残り幅いっぱい (-1) にするため、解除ボタンを先に置く
                if (view.DrawButton("解除", ClearButtonWidth, rowHeight, hasOverride))
                {
                    RecordEdit(target, node.displayName + " 解除");
                    MaidNodeVisibilityController.ClearOverride(target, node.boneName);
                    MaidNodeVisibilityController.Flush(target);
                }

                view.DrawToggle(label, MaidNodeVisibilityController.IsVisible(target, node.boneName),
                    -1, rowHeight,
                    value =>
                    {
                        RecordEdit(target, node.displayName);
                        MaidNodeVisibilityController.SetOverride(target, node.boneName, value);
                        MaidNodeVisibilityController.Flush(target);
                    });
            }
            view.EndLayout();
        }

        public static void RecordEdit(Maid target, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.NodeVisibility, "ノード表示: " + label);
        }
    }
}
