using System;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルのブレンドシェイプ 1 つ分の行描画 (変更追跡チェック付きラベル + 重みスライダー)。
    /// ShapeKeyEditWindow と TimelineItemInspector (メニュー項目選択時) で共有する。
    /// 追跡ストア更新とメッシュへの反映 (FixBlendValues) までここで面倒を見る
    /// </summary>
    public static class ModelShapeKeyRowDrawer
    {
        /// <summary>重みスライダーの範囲。モデルのシェイプキーは負値・誇張も許す</summary>
        private const float MinWeight = -1f;
        private const float MaxWeight = 2f;

        public static void Draw(
            GUIView view,
            MTEP.StudioModelStat model,
            MTEP.ModelBlendShape blendShape,
            float rowHeight)
        {
            var modelObject = model.transform.gameObject;
            var shapeKeyName = blendShape.shapeKeyName;
            var weight = blendShape.weight;

            // 表示判定用。まだ 1 つもチェックしていないモデルのストアを作らないよう FindStore を使う
            // (操作側のコールバックは GetStore で遅延生成する)
            var shapeKeyStore = ModelShapeKeyEditManager.instance.FindStore(modelObject);
            var isModified = shapeKeyStore != null && shapeKeyStore.IsModified(shapeKeyName);

            // 変更追跡チェック。ON=プリセット保存とタイムライン表示の対象。
            // 手動 OFF は「未編集へ戻す」操作なので重みも 0 に戻す
            Action<bool> onCheckChanged = newChecked =>
            {
                if (newChecked)
                {
                    ModelShapeKeyEditManager.instance.GetStore(modelObject).Mark(shapeKeyName);
                }
                else
                {
                    blendShape.weight = 0f;
                    model.FixBlendValues();
                    ModelShapeKeyEditManager.instance.GetStore(modelObject).Unmark(shapeKeyName);
                }
            };

            view.DrawTrackedLabel(isModified, onCheckChanged, shapeKeyName, -1, rowHeight);

            var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
            {
                width = -1,
                min = MinWeight,
                max = MaxWeight,
                step = 0.01f,
                defaultValue = 0f,
                value = weight,
                onChanged = x => weight = x,
            });

            // FixBlendValues は全頂点を走査するため、値が変わったときだけ呼ぶ
            if (updateTransform)
            {
                blendShape.weight = weight;
                model.FixBlendValues();
                // 編集したシェイプキーは自動で追跡対象にする
                ModelShapeKeyEditManager.instance.GetStore(modelObject).Mark(shapeKeyName);
            }
        }
    }
}
