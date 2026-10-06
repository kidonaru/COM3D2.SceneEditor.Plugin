using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 体型レイヤーのメニュー項目 → 項目のスライダー。
    /// 行の描画と履歴への記録は BodySliderRowDrawer に任せる
    /// </summary>
    public class BodySliderItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        private const float ComponentLabelWidth = 40f;

        /// <summary>メニュー項目名 (項目キー) から定義を求める。対象外なら null</summary>
        public static BodySliderItem ResolveItem(string itemName)
        {
            return BodySliderDefs.Find(itemName);
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
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, RowHeight);
                return;
            }

            foreach (var menuItem in items)
            {
                var item = ResolveItem(menuItem.name);
                if (item == null)
                {
                    view.DrawLabel(menuItem.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの項目の行か分かるよう見出しを出す
                TimelineItemClipboardMenu.DrawHeading(view, item.displayName, layer, menuItem.name);
                BodySliderRowDrawer.DrawComponents(view, maid, item, ComponentLabelWidth);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 体型スライダーに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
