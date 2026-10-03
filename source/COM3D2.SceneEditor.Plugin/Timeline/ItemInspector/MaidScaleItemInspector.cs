using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールレイヤーのメニュー項目 → 骨の倍率スライダー。
    /// 行の描画と履歴への記録は MaidScaleRowDrawer に任せる
    /// </summary>
    public class MaidScaleItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名 (骨名) から対象骨を求める。対象外なら null</summary>
        public static MaidScaleBone ResolveBone(string itemName)
        {
            return MaidScaleBones.Find(itemName);
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
                var bone = ResolveBone(item.name);
                if (bone == null)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの骨の行か分かるよう見出しを出す
                TimelineItemClipboardMenu.DrawHeading(view, bone.displayName, layer, item.name);
                MaidScaleRowDrawer.Draw(view, maid, bone, "倍率", 40);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // メイドスケールに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
