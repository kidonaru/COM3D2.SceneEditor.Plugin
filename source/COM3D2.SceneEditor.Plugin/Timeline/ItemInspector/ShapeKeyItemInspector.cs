using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドシェイプレイヤー (ShapeKeyTimelineLayer) のメニュー項目 → シェイプキー重み編集UI。
    /// メニュー項目名はシェイプキー名そのもの
    /// </summary>
    public class ShapeKeyItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            var maidCache = maid != null
                ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            if (maidCache == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            foreach (var item in items)
            {
                var blendShape = maidCache.GetBlendShape(item.name);
                if (!MaidShapeKeyRowDrawer.IsEditable(blendShape))
                {
                    view.DrawLabel(item.displayName + " (このメイドには存在しません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                MaidShapeKeyRowDrawer.Draw(
                    view, maid, maidCache, item.name, blendShape, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // シェイプキーに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
