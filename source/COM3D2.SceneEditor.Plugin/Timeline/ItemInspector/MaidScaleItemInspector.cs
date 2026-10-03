using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールレイヤーのメニュー項目 → 骨の倍率スライダー。
    /// 値は MaidScaleController を通して読み書きし、操作は履歴に記録する
    /// </summary>
    public class MaidScaleItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

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
                DrawScaleSlider(view, maid, bone);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // メイドスケールに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }

        private static void DrawScaleSlider(GUIView view, Maid maid, MaidScaleBone bone)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "倍率",
                labelWidth = 40,
                width = -1,
                min = MaidScaleBones.MinScale,
                max = MaidScaleBones.MaxScale,
                defaultValue = MaidScaleBones.DefaultScale,
                value = scaleController.GetScale(maid, bone.boneName),
                onChanged = newValue =>
                {
                    // ドラッグ中の連続変更は 1 件に集約される
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.MaidScale,
                        "メイドスケール: " + bone.displayName);
                    scaleController.SetScale(maid, bone.boneName, newValue);
                },
            });
        }
    }
}
