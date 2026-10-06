using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 体型スライダーの項目 1 件分のスライダー (表示する成分だけ) と、全項目を既定値へ戻すボタン。
    /// ボーンウィンドウの「体型」タブと TimelineItemInspector (体型レイヤーの項目表示) で共有する。
    /// 値の読み書きは BodySliderController を通し、操作は履歴に記録する
    /// </summary>
    public static class BodySliderRowDrawer
    {
        private const string ResetAllLabel = "すべて既定に戻す";

        private static BodySliderController controller
            => MaidManipulateManager.instance.bodySliderController;

        public static void DrawComponents(GUIView view, Maid maid, BodySliderItem item, float labelWidth)
        {
            var values = controller.GetValues(maid, item.key);
            for (var i = 0; i < item.components.Length; i++)
            {
                var component = item.components[i];
                if (!component.visible)
                {
                    continue;
                }

                var index = i;
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = component.label,
                    labelWidth = labelWidth,
                    width = -1,
                    min = component.min,
                    max = component.max,
                    defaultValue = item.defaultValue,
                    value = values[index],
                    onChanged = newValue =>
                    {
                        RecordEdit(maid, item.displayName);
                        // ドラッグ中に他の成分が変わっていることはないが、書き込み直前の値から作り直す
                        var current = controller.GetValues(maid, item.key);
                        current[index] = newValue;
                        controller.SetValues(maid, item.key, current);
                    },
                });
            }
        }

        /// <summary>
        /// 全項目を既定値へ戻す。履歴は 1 件にまとめる。
        /// 変更していないメイドでは無変更の履歴を積まないよう押せなくする
        /// </summary>
        public static void DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)
        {
            if (!view.DrawButton(ResetAllLabel, width, rowHeight, controller.HasState(maid)))
            {
                return;
            }

            RecordEdit(maid, ResetAllLabel);
            controller.ResetAll(maid);
        }

        /// <summary>体型スライダーの操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid maid, string label)
        {
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.BodySlider, "体型: " + label);
        }
    }
}
