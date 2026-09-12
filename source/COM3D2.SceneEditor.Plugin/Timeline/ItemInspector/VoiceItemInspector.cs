using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドボイスレイヤー (VoiceTimelineLayer) のメニュー項目 → ボイス再生パラメータの編集UI。
    /// 項目は「ボイス」1 つだけ。対象はレイヤーのメイド (サウンドウィンドウの選択とは独立)。
    /// 逆方向: ボイスに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class VoiceItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(layer.maid);
            if (maidCache == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            view.BeginAutoEditMode();

            // 項目は 1 つだけなので、選択内容によらずボイスの行を出す
            VoiceRowDrawer.Draw(view, maidCache, RowHeight);

            view.EndAutoEditMode();
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
