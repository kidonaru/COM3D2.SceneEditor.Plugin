using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 移動レイヤー (MoveTimelineLayer) のメニュー項目 → メイドの Transform 編集UI。
    /// キー化されるのはメイドの GameObject そのものなので、
    /// Inspector の Object 表示と同じ行を出す。
    /// 逆方向: メイドの GameObject 選択 → 移動項目のメニュー選択
    /// </summary>
    public class MoveItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (Object 表示と見た目を揃える)</summary>
        private const float LabelWidth = 50f;
        /// <summary>InspectorWindow.ScaleLabelWidth と同じ値 (連動トグル分を差し引いた幅)</summary>
        private const float ScaleLabelWidth = 25f;

        private readonly ObjectTransformRowDrawer _transformRowDrawer =
            new ObjectTransformRowDrawer();

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

            // 退避中のメイドの Transform は戻り先の値で上書きされるため操作させない
            if (HiddenMaidGuard.DrawWarningIfHidden(
                    view, maid, "非表示中は移動を操作できません", RowHeight))
            {
                return;
            }

            // 項目は「移動」1 つだけなので、選択内容によらずメイドの Transform を出す
            _transformRowDrawer.Draw(
                view, maid.gameObject, LabelWidth, ScaleLabelWidth, RowHeight);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var maid = layer.maid;
            if (maid == null
                || SelectionManager.instance.selectedObject != maid.gameObject)
            {
                return null;
            }
            return MTEP.MoveTimelineLayer.MoveBoneName;
        }
    }
}
