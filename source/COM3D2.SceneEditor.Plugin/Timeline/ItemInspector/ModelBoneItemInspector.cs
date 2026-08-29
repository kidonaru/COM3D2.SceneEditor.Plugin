using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルボーンレイヤー (ModelBoneTimelineLayer) のメニュー項目 → ボーンの Transform 編集UI。
    /// 逆方向: ボーン (またはその子) の選択 → 該当ボーンのメニュー選択
    /// </summary>
    public class ModelBoneItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (ボーン表示と見た目を揃える)</summary>
        private const float LabelWidth = 50f;
        /// <summary>InspectorWindow.ScaleLabelWidth と同じ値 (連動トグル分を差し引いた幅)</summary>
        private const float ScaleLabelWidth = 25f;

        private static MTEP.StudioModelManager modelManager
            => MTEP.StudioModelManager.instance;

        private readonly ItemRowDrawerCache<ModelBoneRowDrawer> _boneRowDrawers =
            new ItemRowDrawerCache<ModelBoneRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                var bone = modelManager.GetBone(item.name);
                var model = bone != null ? bone.model : null;
                if (bone == null || bone.transform == null
                    || model == null || model.transform == null)
                {
                    // モデル差し替え・削除の直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (ボーンが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのボーンの行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);
                _boneRowDrawers.Get(item.name).Draw(
                    view, model.transform.gameObject, bone.transform,
                    LabelWidth, ScaleLabelWidth, RowHeight);
            }

            _boneRowDrawers.PruneExcept(items);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            if (selectedObject == null)
            {
                return null;
            }

            // ボーンそのものが選ばれたときだけ反応する。
            // 祖先まで辿るとモデル本体の選択でも先頭のボーンに当たってしまい、
            // モデルレイヤーの逆引きと食い違うため
            var selected = selectedObject.transform;
            foreach (var model in modelManager.models)
            {
                foreach (var bone in model.bones)
                {
                    if (bone.transform == selected)
                    {
                        return bone.name;
                    }
                }
            }
            return null;
        }
    }
}
