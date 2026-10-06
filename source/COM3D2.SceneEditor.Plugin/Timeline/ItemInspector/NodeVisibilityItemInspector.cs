using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ノード表示レイヤーのメニュー項目 → 表示トグル。
    /// 行の描画と履歴への記録は NodeVisibilityRowDrawer に任せる
    /// </summary>
    public class NodeVisibilityItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名 (ボーン名) からノードを求める。対象外なら null</summary>
        public static MaidNodeVisibilityNode ResolveNode(string itemName)
        {
            return MaidNodeVisibilityNodes.Find(itemName);
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }
            if (!MaidNodeVisibilityController.IsBodyReady(maid))
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var item in items)
            {
                var node = ResolveNode(item.name);
                if (node == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }
                NodeVisibilityRowDrawer.Draw(view, maid, node, RowHeight, 0f);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // ノード表示に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
