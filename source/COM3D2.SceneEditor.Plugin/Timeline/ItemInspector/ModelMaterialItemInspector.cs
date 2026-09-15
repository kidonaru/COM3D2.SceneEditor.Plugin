using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>モデルマテリアルレイヤー (ModelMaterialTimelineLayer) のメニュー項目 → マテリアル編集UI</summary>
    public class ModelMaterialItemInspector : MaterialItemInspectorBase
    {
        protected override MTEP.ModelMaterial FindMaterial(
            MTEP.ITimelineLayer layer, string itemName)
        {
            return MTEP.StudioModelManager.instance.GetMaterial(itemName);
        }

        protected override MaterialTrackTarget CreateTrack(
            MTEP.ITimelineLayer layer, MTEP.ModelMaterial material)
        {
            var model = material.model;
            if (model == null || model.transform == null)
            {
                return new MaterialTrackTarget();
            }

            var modelObject = model.transform.gameObject;
            return new MaterialTrackTarget
            {
                findStore = () => ModelMaterialEditManager.instance.FindStore(modelObject),
                getStore = () => ModelMaterialEditManager.instance.GetStore(modelObject),
                // 修飾名は group 振り直しで変わるため、記録は Unity マテリアル名で持つ
                getKey = m => m.displayName,
            };
        }
    }
}
