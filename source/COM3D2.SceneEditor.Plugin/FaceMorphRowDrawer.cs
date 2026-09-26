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
        /// <param name="drawTrailing">
        /// 行の右端に置く要素 (Inspector のコピー / 貼り付けメニュー)。null なら従来どおりの行を描く
        /// </param>
        public static void Draw(
            GUIView view, Maid target, FaceMorphDef def, float labelWidth, float rowHeight,
            Action<GUIView> drawTrailing = null)
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

            Action<bool> onToggleChanged = newValue =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                    "表情: " + def.displayName);
                // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                MaidFaceMorphController.SetMabataki(target, false);
                MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                FaceEditManager.instance.GetStore(target).Mark(def.name);
            };

            var sliderOption = new GUIView.SliderOption
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
            };

            if (drawTrailing != null)
            {
                // GUIView.DrawTrackedToggle / DrawTrackedSliderValue と同じ並びを展開し、右端に要素を足す
                // (共有サブモジュールの GUIView には手を入れない)。あちらの並びが変わったらここも合わせる
                view.BeginHorizontal();
                {
                    view.DrawToggle(isModified, GUIView.TrackedCheckWidth, rowHeight, onCheckChanged);
                    if (def.isToggle)
                    {
                        view.DrawToggle(def.displayName, value >= 0.5f, 130, rowHeight, onToggleChanged);
                    }
                    else
                    {
                        // 右端の要素ぶんスライダーを縮める (-1 だと右端まで伸びて重なる)
                        sliderOption.width = ItemClipboardMenu.GetRemainingWidth(view);
                        view.DrawSliderValue(sliderOption);
                    }
                    drawTrailing(view);
                }
                view.EndLayout();
                return;
            }

            if (def.isToggle)
            {
                view.DrawTrackedToggle(isModified, onCheckChanged,
                    def.displayName, value >= 0.5f, 130, rowHeight, onToggleChanged);
            }
            else
            {
                view.DrawTrackedSliderValue(isModified, onCheckChanged, rowHeight, sliderOption);
            }
        }
    }
}
