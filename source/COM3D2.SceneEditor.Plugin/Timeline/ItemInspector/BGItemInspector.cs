using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景レイヤー (BGTimelineLayer) のメニュー項目 → 背景の Transform 編集UI。
    /// 項目は背景 1 種につき 1 行だが、実体を持つのは現在の背景だけなので、
    /// それ以外の項目は編集できない旨を出す。
    /// 逆方向: 背景オブジェクト (またはその子) の選択 → 現在の背景のメニュー選択
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
            var currentBgName = mgr != null ? mgr.GetBGName() : null;

            foreach (var item in items)
            {
                if (bgObject == null)
                {
                    // シーン遷移中など背景の実体がまだ無い状態
                    view.DrawLabel(item.displayName + " (背景を読み込み中です)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                if (item.name != currentBgName)
                {
                    // 別の背景のキーだけが残っている項目。切り替えるまで実体が無い
                    view.DrawLabel(item.displayName + " (現在の背景ではありません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                view.DrawLabel(item.displayName, -1, RowHeight);
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
                ? mgr.GetBGName()
                : null;
        }
    }
}
