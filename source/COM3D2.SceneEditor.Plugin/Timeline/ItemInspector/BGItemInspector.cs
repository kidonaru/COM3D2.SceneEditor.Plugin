using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景レイヤー (BGTimelineLayer) のメニュー項目 → 背景の Transform 編集UI。
    /// 項目は固定 1 行で、現在の背景名と背景オブジェクトの Transform を編集する。
    /// 逆方向: 背景オブジェクト (またはその子) の選択 → 背景項目のメニュー選択
    /// </summary>
    public class BGItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>BackgroundWindow の LABEL_WIDTH と同じ値</summary>
        private const float LabelWidth = 70f;

        private static BgMgr bgMgr
        {
            get
            {
                var gameMain = GameMain.Instance;
                return gameMain != null ? gameMain.BgMgr : null;
            }
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var mgr = bgMgr;
            var bgObject = mgr != null ? mgr.current_bg_object : null;

            foreach (var item in items)
            {
                if (bgObject == null)
                {
                    // 背景なし、またはシーン遷移中で実体が無い状態
                    view.DrawLabel(item.displayName + " (背景が表示されていません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 背景なし (空文字) は GetDisplayName が空文字を返すので表示名を補う
                var bgName = mgr.GetBGName();
                var bgDisplayName = string.IsNullOrEmpty(bgName)
                    ? "背景なし"
                    : MTEP.PhotoBGManager.instance.GetDisplayName(bgName);
                view.DrawLabel(item.displayName + ": " + bgDisplayName, -1, RowHeight);
                BackgroundRowDrawer.DrawBgTransformRows(
                    view, bgObject.transform, LabelWidth, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            var mgr = bgMgr;
            var bgObject = mgr != null ? mgr.current_bg_object : null;
            if (selectedObject == null || bgObject == null)
            {
                return null;
            }

            // ビューポートのクリックでは背景の子メッシュがヒットしうるため祖先も辿る
            // (IsChildOf は自分自身にも真を返すので、背景ルートの選択もここで拾える)
            return selectedObject.transform.IsChildOf(bgObject.transform)
                ? MTEP.BGTimelineLayer.BGBoneName
                : null;
        }
    }
}
