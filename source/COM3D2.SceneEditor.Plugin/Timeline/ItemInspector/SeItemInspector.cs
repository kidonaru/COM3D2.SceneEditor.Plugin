using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 効果音レイヤー (SeTimelineLayer) のメニュー項目 → 効果音の再生パラメータの編集UI。
    /// 項目は「SE」1 つだけ。
    /// 逆方向: 効果音に対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class SeItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private readonly SeRowDrawer _seRowDrawer = new SeRowDrawer();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            if (timeline == null)
            {
                view.DrawLabel("タイムライン読込後に使用できます", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (サウンドウィンドウと同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみ効果音を操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            // 項目は 1 つだけなので、選択内容によらず SE の行を出す
            _seRowDrawer.Draw(view, timeline, RowHeight);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
