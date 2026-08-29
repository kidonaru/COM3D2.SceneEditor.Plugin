using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 配置モデルレイヤー (ModelTimelineLayer) のメニュー項目 → モデルの Transform 編集UI。
    /// 逆方向: モデル (またはその子) の選択 → 該当モデルのメニュー選択
    /// </summary>
    public class ModelItemInspector : ModelTransformItemInspectorBase<MTEP.StudioModelStat>
    {
        private static MTEP.StudioModelManager modelManager
            => MTEP.StudioModelManager.instance;

        protected override MTEP.StudioModelStat FindModel(string itemName)
        {
            return modelManager.GetModel(itemName);
        }

        protected override List<MTEP.StudioModelStat> models => modelManager.models;
    }
}
