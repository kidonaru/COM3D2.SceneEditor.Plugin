using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表情レイヤー (MorphTimelineLayer) のメニュー項目 → 表情モーフ編集UI</summary>
    public class MorphItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>MaidWindowBase.LABEL_WIDTH (70) と同値 (表情ウィンドウと見た目を揃える。protected のため参照できず値を持つ)</summary>
        private const float LabelWidth = 70f;

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

            foreach (var item in items)
            {
                var def = MaidFaceMorphController.FindDef(item.name);
                if (def == null)
                {
                    // タイムライン専用モーフ (SE の表情ウィンドウに定義が無い) は編集UIを持たない
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }
                if (!MaidFaceMorphController.IsAvailable(maid, def))
                {
                    view.DrawLabel(def.displayName + " (このメイドには存在しません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }
                FaceMorphRowDrawer.Draw(view, maid, def, LabelWidth, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // モーフに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
