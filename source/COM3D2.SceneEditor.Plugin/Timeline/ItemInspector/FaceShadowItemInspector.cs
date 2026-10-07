using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔の影レイヤーのメニュー項目 → トグル。行の描画と履歴への記録は FaceShadowRowDrawer に任せる</summary>
    public class FaceShadowItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        public void DrawItems(GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            if (maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight, textColor: Color.yellow);
                return;
            }

            foreach (var menuItem in items)
            {
                var item = FaceShadowItems.Find(menuItem.name);
                if (item == null)
                {
                    view.DrawLabel(menuItem.displayName + " (未対応)", -1, RowHeight, textColor: Color.gray);
                    continue;
                }
                FaceShadowRowDrawer.Draw(view, maid, item, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 顔の影に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
