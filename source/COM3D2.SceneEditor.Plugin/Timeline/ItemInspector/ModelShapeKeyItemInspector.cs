using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルシェイプレイヤー (ModelShapeKeyTimelineLayer) のメニュー項目
    /// → ブレンドシェイプ重み編集UI。
    /// メニュー項目名は "モデル名/シェイプキー名" 形式
    /// </summary>
    public class ModelShapeKeyItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.StudioModelManager modelManager
            => MTEP.StudioModelManager.instance;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                var blendShape = modelManager.GetBlendShape(item.name);
                var model = blendShape != null ? blendShape.model : null;
                if (model == null || model.transform == null)
                {
                    // モデル差し替え・削除の直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (モデルが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 同名シェイプキーを持つモデルを同時選択したときに見分けられるよう、
                // 行の前にモデル名を出す (項目名にはモデル名が入らない)
                view.DrawLabel(model.displayName, -1, RowHeight);
                ModelShapeKeyRowDrawer.Draw(view, model, blendShape, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // シェイプキーに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
