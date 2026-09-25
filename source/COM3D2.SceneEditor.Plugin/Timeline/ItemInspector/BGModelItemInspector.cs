using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデルレイヤー (BGModelTimelineLayer) のメニュー項目・選択中の背景モデル本体 → モデルの Transform 編集UI。
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
        /// 背景モデルは配置数の増減を背景ウィンドウが持つため、管理行は出さずヘッダー行の表示切替だけにする
        /// </summary>
        protected override void DrawModelHeaderRow(GUIView view, MTEP.BGModelStat model)
        {
            InspectorHeaderRowDrawer.Draw(view, model.visible, model.displayName, RowHeight,
                newValue => { model.visible = newValue; },
                model.transform.gameObject);
        }
    }
}
