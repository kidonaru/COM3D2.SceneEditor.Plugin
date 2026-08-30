using System;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのシェイプキー 1 つ分の行描画 (変更追跡チェック付きラベル + 重みスライダー)。
    /// ShapeKeyEditWindow と TimelineItemInspector (メニュー項目選択時) で共有する。
    /// 追跡ストア更新と morph の反映 (FixBlendValues) までここで面倒を見る
    /// </summary>
    public static class MaidShapeKeyRowDrawer
    {
        /// <summary>重みスライダーの上限。シェイプキーは 1 を超えて誇張できる</summary>
        private const float MaxWeight = 2f;

        /// <summary>
        /// 行を描けるシェイプキーか。
        /// 対象メイドが該当 morph を持たない場合 (着替え後など) は
        /// null か entities 空で返るため、スライダーを出さない
        /// </summary>
        public static bool IsEditable(MTEP.MaidBlendShape blendShape)
        {
            return blendShape != null && blendShape.entities.Count > 0;
        }

        public static void Draw(
            GUIView view,
            Maid target,
            MTEP.MaidCache maidCache,
            string shapeKeyName,
            MTEP.MaidBlendShape blendShape,
            float rowHeight)
        {
            var weight = blendShape.weight;
            // 表示判定用。まだ 1 つもチェックしていないメイドのストアを作らないよう FindStore を使う
            // (操作側のコールバックは GetStore で遅延生成する)
            var shapeKeyStore = MaidShapeKeyEditManager.instance.FindStore(target);
            var isModified = shapeKeyStore != null && shapeKeyStore.IsModified(shapeKeyName);

            // 変更追跡チェック。ON=プリセット保存とタイムラインのキーフレーム対象。
            // 手動 OFF は「未編集へ戻す」操作なので値も 0 に戻す
            Action<bool> onCheckChanged = newChecked =>
            {
                if (newChecked)
                {
                    MaidShapeKeyEditManager.instance.GetStore(target).Mark(shapeKeyName);
                }
                else
                {
                    blendShape.weight = 0f;
                    maidCache.FixBlendValues(new string[] { shapeKeyName });
                    MaidShapeKeyEditManager.instance.GetStore(target).Unmark(shapeKeyName);
                }
            };

            view.DrawTrackedLabel(isModified, onCheckChanged, shapeKeyName, -1, rowHeight);

            var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
            {
                width = -1,
                min = 0f,
                max = MaxWeight,
                step = 0.01f,
                defaultValue = 0f,
                value = weight,
                onChanged = x => weight = x,
            });

            if (updateTransform)
            {
                blendShape.weight = weight;
                maidCache.FixBlendValues(new string[] { shapeKeyName });
                // 編集したシェイプキーは自動で追跡対象にする
                MaidShapeKeyEditManager.instance.GetStore(target).Mark(shapeKeyName);
            }
        }
    }
}
