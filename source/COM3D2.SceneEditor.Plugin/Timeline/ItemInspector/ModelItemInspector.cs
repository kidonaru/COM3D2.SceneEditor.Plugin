using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 配置モデルレイヤー (ModelTimelineLayer) のメニュー項目 → モデルの管理操作と Transform 編集UI。
    /// 逆方向: モデル (またはその子) の選択 → 該当モデルのメニュー選択
    /// </summary>
    public class ModelItemInspector : ModelTransformItemInspectorBase<MTEP.StudioModelStat>
    {
        private static MTEP.StudioModelManager modelManager
            => MTEP.StudioModelManager.instance;

        private readonly ItemRowDrawerCache<ModelManageRowDrawer> _manageRowDrawers =
            new ItemRowDrawerCache<ModelManageRowDrawer>();

        protected override MTEP.StudioModelStat FindModel(string itemName)
        {
            return modelManager.GetModel(itemName);
        }

        protected override List<MTEP.StudioModelStat> models => modelManager.models;

        protected override void DrawModelManageRows(GUIView view, MTEP.StudioModelStat model)
        {
            _manageRowDrawers.Get(model.name).Draw(view, model);
        }

        protected override void PruneCaches(IList<MTEP.IBoneMenuItem> items)
        {
            _manageRowDrawers.PruneExcept(items);
        }
    }
}
