using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデルレイヤー (BGModelTimelineLayer) のメニュー項目 → モデルの Transform 編集UI。
    /// 逆方向: 背景モデル (またはその子) の選択 → 該当モデルのメニュー選択
    /// </summary>
    public class BGModelItemInspector : ModelTransformItemInspectorBase<MTEP.BGModelStat>
    {
        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;

        protected override MTEP.BGModelStat FindModel(string itemName)
        {
            return bgModelManager.GetModel(itemName);
        }

        protected override List<MTEP.BGModelStat> models => bgModelManager.models;
    }
}
