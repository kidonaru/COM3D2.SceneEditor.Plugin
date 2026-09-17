using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデルマテリアルレイヤー (BGModelMaterialTimelineLayer) のメニュー項目
    /// → マテリアル編集UI。背景モデルは追跡ストアを持たないため追跡チェックは出ない
    /// (マテリアルウィンドウの背景タブと同じ)
    /// </summary>
    public class BGModelMaterialItemInspector : MaterialItemInspectorBase
    {
        protected override MTEP.ModelMaterial FindMaterial(
            MTEP.ITimelineLayer layer, string itemName)
        {
            return MTEP.BGModelManager.instance.GetMaterial(itemName);
        }

        protected override MaterialTrackTarget CreateTrack(
            MTEP.ITimelineLayer layer, MTEP.ModelMaterial material)
        {
            return new MaterialTrackTarget();
        }
    }
}
