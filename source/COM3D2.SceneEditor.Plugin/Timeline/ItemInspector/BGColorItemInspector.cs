using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景色レイヤー (BGColorTimelineLayer) のメニュー項目 → 背景色 / 地面の編集UI。
    /// 項目は「背景色」と「地面色」の 2 つで、地面色は色だけでなく
    /// レイヤーがキー化する表示・位置・広さもまとめて出す (背景ウィンドウと同じ行)。
    /// 逆方向: 背景色・地面に対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class BGColorItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>BackgroundWindow の LABEL_WIDTH と同じ値</summary>
        private const float LabelWidth = 70f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                if (item.name == MTEP.BGColorTimelineLayer.BGColorBoneName)
                {
                    // 色行が「背景色」ラベルを自前で描くため、見出しは重ねない
                    BackgroundRowDrawer.DrawBgColorRow(view, RowHeight);
                }
                else if (item.name == MTEP.BGColorTimelineLayer.BGGroundColorBoneName)
                {
                    // 地面は複数行が並ぶため、まとまりが分かるよう見出しを出す
                    view.DrawLabel(item.displayName, -1, RowHeight);
                    BackgroundRowDrawer.DrawGroundRows(view, LabelWidth, RowHeight);
                }
                else
                {
                    // レイヤーが持つ項目は上記 2 つだけ。増えたらここに来る
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                }
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
