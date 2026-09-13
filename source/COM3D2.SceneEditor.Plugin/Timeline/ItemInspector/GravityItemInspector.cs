using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力レイヤー (GravityTimelineLayer) のメニュー項目 → 重力カテゴリの編集UI。
    /// 重力ウィンドウと同じ行 (GravityRowDrawer) を出す
    /// </summary>
    public class GravityItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名 (GravityCategory.id) からカテゴリを求める。未知なら null</summary>
        public static GravityCategory ResolveCategory(string itemName)
        {
            return MaidGravityController.FindCategory(itemName);
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

            foreach (var item in items)
            {
                var category = ResolveCategory(item.name);
                if (category == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのカテゴリの行か分かるよう見出しを出す
                view.DrawLabel(category.name, -1, RowHeight);
                GravityRowDrawer.Draw(view, maid, category, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 重力に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
