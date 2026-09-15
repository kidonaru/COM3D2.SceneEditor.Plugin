using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表情モーフ 1 つ分の行描画 (変更追跡チェック + スライダー/トグル)。
    /// MaidFaceWindow と TimelineItemInspector (Inspector の項目表示) で共有する。
    /// 履歴登録・まばたき停止・追跡ストア更新までここで面倒を見る
    /// </summary>
    public static class FaceMorphRowDrawer
    {
        public static void Draw(
            GUIView view, Maid target, FaceMorphDef def, float labelWidth, float rowHeight)
        {
            var value = MaidFaceMorphController.GetMorphValue(target, def);
            // 表示判定用。まだ 1 つも編集していないメイドのストアを作らないよう FindStore を使う
            // (編集操作側のコールバックは GetStore で遅延生成する)
            var faceStore = FaceEditManager.instance.FindStore(target);
            var isModified = faceStore != null && faceStore.IsModified(def.name);

            // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
            // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
            Action<bool> onCheckChanged = newChecked =>
            {
                if (newChecked)
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                        "表情変更マーク: " + def.displayName);
                    FaceEditManager.instance.GetStore(target).Mark(def.name);
                }
                else
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                        "表情変更解除: " + def.displayName);
                    MaidFaceMorphController.SetMabataki(target, false);
                    MaidFaceMorphController.SetMorphValue(target, def, 0f);
                    FaceEditManager.instance.GetStore(target).Unmark(def.name);
                }
            };

            if (def.isToggle)
            {
                view.DrawTrackedToggle(isModified, onCheckChanged,
                    def.displayName, value >= 0.5f, 130, rowHeight, newValue =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情: " + def.displayName);
                        // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                        MaidFaceMorphController.SetMabataki(target, false);
                        MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                        FaceEditManager.instance.GetStore(target).Mark(def.name);
                    });
            }
            else
            {
                view.DrawTrackedSliderValue(isModified, onCheckChanged, rowHeight,
                    new GUIView.SliderOption
                    {
                        label = def.displayName,
                        labelWidth = labelWidth - GUIView.TrackedCheckWidth,
                        width = -1,
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = value,
                        onChanged = newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情: " + def.displayName);
                            // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, newValue);
                            FaceEditManager.instance.GetStore(target).Mark(def.name);
                        },
                    });
            }
        }
    }
}
