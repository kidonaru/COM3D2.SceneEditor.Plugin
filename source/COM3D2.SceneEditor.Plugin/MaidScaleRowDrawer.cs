using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールの骨 1 本分の倍率スライダーと、全骨を元の大きさへ戻すボタン。
    /// ボーンウィンドウの「腕スケール」タブと TimelineItemInspector (メイドスケールレイヤーの項目表示) で共有する。
    /// 値の読み書きは MaidScaleController を通し、操作は履歴に記録する
    /// </summary>
    public static class MaidScaleRowDrawer
    {
        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

        public static void Draw(GUIView view, Maid maid, MaidScaleBone bone, string label, float labelWidth)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = MaidScaleBones.MinScale,
                max = MaidScaleBones.MaxScale,
                defaultValue = MaidScaleBones.DefaultScale,
                value = scaleController.GetScale(maid, bone.boneName),
                onChanged = newValue =>
                {
                    RecordEdit(maid, bone.displayName);
                    scaleController.SetScale(maid, bone.boneName, newValue);
                },
            });
        }

        /// <summary>
        /// 6 本すべてを元の大きさへ戻す。履歴は 1 件にまとめる。
        /// 拡縮していないメイドでは無変更の履歴を積まないよう押せなくする
        /// </summary>
        public static void DrawResetAll(GUIView view, Maid maid, float width, float rowHeight)
        {
            if (!view.DrawButton("すべて 1 に戻す", width, rowHeight, scaleController.HasState(maid)))
            {
                return;
            }

            RecordEdit(maid, "すべて 1 に戻す");
            foreach (var bone in MaidScaleBones.bones)
            {
                scaleController.SetScale(maid, bone.boneName, MaidScaleBones.DefaultScale);
            }
        }

        /// <summary>メイドスケールの操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid maid, string label)
        {
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.MaidScale, "メイドスケール: " + label);
        }
    }
}
