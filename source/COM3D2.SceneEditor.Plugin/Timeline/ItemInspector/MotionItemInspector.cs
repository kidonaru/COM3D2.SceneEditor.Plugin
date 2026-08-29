using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドアニメレイヤー (MotionTimelineLayer) のメニュー項目 → ボーン編集UI。
    /// 逆方向: Inspector のボーン選択 → 該当ボーン行のメニュー選択
    /// </summary>
    public class MotionItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (見た目を揃える)</summary>
        private const float LabelWidth = 50f;

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

            if (HiddenMaidGuard.DrawWarningIfHidden(
                    view, maid, "非表示中はポーズを操作できません", RowHeight))
            {
                return;
            }

            foreach (var item in items)
            {
                var def = MaidBoneSliderController.FindDef(item.name);
                if (def == null)
                {
                    // 拡張ボーン等、ポーズスライダー定義が無いボーンは S0 では対象外
                    view.DrawLabel(item.displayName + " (スライダー未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのボーンの行か分かるよう見出しを出す
                view.DrawLabel(def.displayName, -1, RowHeight);
                BoneSliderRowDrawer.Draw(view, maid, def, LabelWidth);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selection = SelectionManager.instance;
            if (!selection.hasBoneSelection || selection.selectedBoneMaid != layer.maid)
            {
                return null;
            }
            // MaidBoneMenuItem.name は標準ボーン名なので boneName がそのまま逆引きキーになる
            return selection.selectedBoneDef.boneName;
        }
    }
}
