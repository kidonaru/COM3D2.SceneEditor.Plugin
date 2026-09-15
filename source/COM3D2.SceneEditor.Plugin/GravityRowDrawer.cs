using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力カテゴリ 1 件分の行描画 (有効トグル・リセット・XYZ スライダー)。
    /// 重力ウィンドウと TimelineItemInspector (重力レイヤーの項目表示) で共有する。
    /// 値の読み書きは MaidGravityController を通し、操作は履歴に記録する
    /// </summary>
    public static class GravityRowDrawer
    {
        private static MaidGravityController gravityController
            => MaidManipulateManager.instance.gravityController;

        public static void Draw(GUIView view, Maid target, GravityCategory category, float rowHeight)
        {
            if (!gravityController.IsValid(target, category))
            {
                // 着ていない・揺れものを持たない衣装では力の掛け先が無い
                view.DrawLabel("対象の揺れものがありません", -1, rowHeight);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawToggle("有効", gravityController.GetEnabled(target, category),
                    80, rowHeight, true,
                    value =>
                    {
                        RecordEdit(target, category, "有効");
                        gravityController.SetEnabled(target, category, value);
                    });

                if (view.DrawButton("リセット", 80, rowHeight))
                {
                    RecordEdit(target, category, "リセット");
                    gravityController.SetOffset(target, category, Vector3.zero);
                }
            }
            view.EndLayout();

            var offset = gravityController.GetOffset(target, category);
            DrawAxisSlider(view, target, category, "X", offset.x,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.x = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(view, target, category, "Y", offset.y,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.y = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(view, target, category, "Z", offset.z,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.z = value;
                    gravityController.SetOffset(target, category, current);
                });
        }

        /// <summary>共通書式のスライダー 1 行 (LightWindow と同形式)</summary>
        private static void DrawAxisSlider(
            GUIView view, Maid target, GravityCategory category, string label, float value,
            Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = 20,
                width = -1,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = newValue =>
                {
                    RecordEdit(target, category, label);
                    onChanged(newValue);
                },
            });
        }

        /// <summary>重力操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid target, GravityCategory category, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Gravity,
                "重力: " + category.name + " " + label);
        }
    }
}
