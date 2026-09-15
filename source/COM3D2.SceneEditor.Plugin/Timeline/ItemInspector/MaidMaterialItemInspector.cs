using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドマテリアルレイヤー (MaidMaterialTimelineLayer) のメニュー項目 → マテリアル編集UI</summary>
    public class MaidMaterialItemInspector : MaterialItemInspectorBase
    {
        protected override MTEP.ModelMaterial FindMaterial(
            MTEP.ITimelineLayer layer, string itemName)
        {
            var maid = layer.maid;
            var maidCache = maid != null
                ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            return maidCache != null ? maidCache.GetMaterial(itemName) : null;
        }

        protected override MaterialTrackTarget CreateTrack(
            MTEP.ITimelineLayer layer, MTEP.ModelMaterial material)
        {
            var maid = layer.maid;
            return new MaterialTrackTarget
            {
                findStore = () => MaidMaterialEditManager.instance.FindStore(maid),
                getStore = () => MaidMaterialEditManager.instance.GetStore(maid),
                // タイムライン側の候補名 (MaidCache.materialNames) と同じ文字列
                getKey = m => m.name,
            };
        }
    }
}
