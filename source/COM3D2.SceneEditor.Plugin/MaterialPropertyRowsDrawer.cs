using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル 1 件の変更追跡先。対象種別 (メイド / モデル / 背景モデル) で
    /// ストアと記録キーが変わるため、呼び出し側から差し込む。
    /// 3 つとも未設定なら追跡なし (背景モデルは追跡ストアを持たない)
    /// </summary>
    public struct MaterialTrackTarget
    {
        /// <summary>表示判定用。まだ 1 つもチェックしていない対象のストアを作らないため FindStore を使う</summary>
        public Func<EditTargetStore> findStore;

        /// <summary>操作時のストア。遅延生成する</summary>
        public Func<EditTargetStore> getStore;

        /// <summary>記録する生名。メイドは ModelMaterial.name、モデルは displayName</summary>
        public Func<MTEP.ModelMaterial, string> getKey;

        public bool isEnabled => findStore != null && getStore != null && getKey != null;
    }

    /// <summary>
    /// マテリアル 1 件の色 / 数値プロパティの行。
    /// MaterialEditWindow と TimelineItemInspector (メニュー項目選択時) で共有する。
    /// 対象種別によらず中身は同じなので 3 系統で共用できる。
    /// スクロールビューは呼び出し側の持ち物 (Inspector は複数マテリアルを 1 つの
    /// スクロールに並べるため、ここで入れ子にしない)
    /// </summary>
    public static class MaterialPropertyRowsDrawer
    {
        /// <param name="colorLabelPrefix">
        /// 色行のラベルに付ける接頭辞。null なら付けない。
        /// このラベルは ColorPickerWindow が編集対象を同定するキーも兼ねるため、
        /// 複数マテリアルを並べる呼び出し側 (Inspector) はマテリアルを識別できる値を渡すこと。
        /// 同じキーが並ぶと後から描いた行がピッカーの反映先を奪う
        /// </param>
        public static void Draw(
            GUIView view,
            MTEP.ModelMaterial material,
            MaterialTrackTarget track,
            float rowHeight,
            string colorLabelPrefix)
        {
            var defaultTrans = MTEP.TransformDataModelMaterial.defaultTrans;
            var trackKey = track.isEnabled ? track.getKey(material) : null;

            // 編集されたマテリアルは自動で追跡対象にする
            Action markTracked = () =>
            {
                if (trackKey != null)
                {
                    track.getStore().Mark(trackKey);
                }
            };

            if (trackKey != null)
            {
                var store = track.findStore();
                var isModified = store != null && store.IsModified(trackKey);

                // 変更追跡チェック。ON=タイムラインの表示とキー書き込みの対象。
                // 手動 OFF は「未編集へ戻す」操作なので値も初期値へ戻す
                Action<bool> onCheckChanged = newChecked =>
                {
                    if (newChecked)
                    {
                        track.getStore().Mark(trackKey);
                    }
                    else
                    {
                        material.Reset();
                        track.getStore().Unmark(trackKey);
                    }
                };

                view.DrawTrackedLabel(
                    isModified, onCheckChanged, material.displayName, -1, rowHeight);
            }

            if (view.DrawButton("初期化", 80, rowHeight))
            {
                material.Reset();
                // 初期値へ戻したのだから追跡からも外す (チェック OFF と同じ意味)
                if (trackKey != null)
                {
                    track.getStore().Unmark(trackKey);
                }
            }

            foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
            {
                if (!material.HasColor(propertyType))
                {
                    continue;
                }

                var color = material.GetColor(propertyType);
                var initialColor = material.GetInitialColor(propertyType);

                // ColorPickerWindow はラベル文字列で編集対象を同定するため、
                // 行ごとに一意な名前を渡す (空文字だと全行が「編集中」扱いになり、
                // ピッカーの反映先も最後の行へ化ける)。ラベル描画は DrawColor 内で行われる
                var colorLabel = colorLabelPrefix == null
                    ? propertyType.ToString()
                    : colorLabelPrefix + "/" + propertyType;
                var cache = view.GetColorFieldCache(colorLabel, true);

                view.DrawColor(cache, color, initialColor,
                    newColor =>
                    {
                        material.SetColor(propertyType, newColor);
                        markTracked();
                    });
            }

            foreach (var propertyType in MTEP.ModelMaterial.ValuePropertyTypes)
            {
                if (!material.HasValue(propertyType))
                {
                    continue;
                }

                var value = material.GetValue(propertyType);
                var initialValue = material.GetInitialValue(propertyType);
                var info = defaultTrans.GetCustomValueInfo(propertyType);

                // _OutlineWidth は 0.001 前後の極小値のため桁数を増やす
                var fieldType = propertyType == MTEP.ModelMaterial.ValuePropertyType._OutlineWidth
                    ? FloatFieldType.F4
                    : FloatFieldType.Float;

                view.DrawLabel($"{propertyType} ({info.name})", -1, rowHeight);

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    fieldType = fieldType,
                    width = -1,
                    min = info.min,
                    max = info.max,
                    step = info.step,
                    defaultValue = initialValue,
                    value = value,
                    onChanged = newValue =>
                    {
                        material.SetValue(propertyType, newValue);
                        markTracked();
                    },
                });
            }
        }
    }
}
