using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデルレイヤー (BGModelTimelineLayer) のメニュー項目 → モデルの Transform 編集UI。
    /// 逆方向: 背景モデル (またはその子) の選択 → 該当モデルのメニュー選択
    /// </summary>
    public class BGModelItemInspector : ModelTransformItemInspectorBase<MTEP.BGModelStat>
    {
        private const float RowHeight = 20f;

        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;

        protected override MTEP.BGModelStat FindModel(string itemName)
        {
            return bgModelManager.GetModel(itemName);
        }

        protected override List<MTEP.BGModelStat> models => bgModelManager.models;

        /// <summary>
        /// 背景モデルは配置数の増減を背景ウィンドウが持つため、
        /// ここは旧レイヤー編集ウィンドウと同じく表示切替だけを出す
        /// </summary>
        protected override void DrawModelManageRows(GUIView view, MTEP.BGModelStat model)
        {
            view.DrawToggle("表示", model.visible, 80, RowHeight, newValue =>
            {
                model.visible = newValue;
            });
        }
    }
}
