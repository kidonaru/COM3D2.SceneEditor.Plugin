using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポーズボーン 1 本分の軸オフセットスライダー行。
    /// InspectorWindow (ボーン選択時) と TimelineItemInspector (メニュー項目選択時) で共有する。
    /// 履歴登録・モーション停止までここで面倒を見る
    /// </summary>
    public static class BoneSliderRowDrawer
    {
        public static void Draw(GUIView view, Maid maid, BoneSliderDef def, float labelWidth)
        {
            // 再生中は基準ポーズが定まらないため値を読まず、操作された瞬間に停止して書き込む
            var offset = MaidMotionState.IsPlaying(maid)
                ? Vector3.zero
                : MaidBoneSliderController.GetOffset(maid, def);

            for (var i = 0; i < def.axes.Length; i++)
            {
                var axisIndex = i;
                var axis = def.axes[i];

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = axis.label,
                    labelWidth = labelWidth,
                    width = -1,
                    min = axis.min,
                    max = axis.max,
                    step = 0.1f,
                    defaultValue = 0f,
                    value = offset[axisIndex],
                    onChanged = value =>
                    {
                        MaidMotionState.StopMotion(maid);
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ボーン回転: " + def.displayName,
                            new[] { MaidBoneSliderController.GetBone(maid, def.boneName) });
                        MaidBoneSliderController.SetOffsetAxis(
                            maid, def, axisIndex, value);
                    },
                });
            }
        }
    }
}
