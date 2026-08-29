using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン視線 (注視先 / 瞳回転) の行描画。
    /// MaidFaceWindow の視線タブと TimelineItemInspector (瞳レイヤーの項目表示) で共有する。
    /// 書き込み先はどのスナップショットにも含まれない MaidCache のため履歴は記録しない。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class TimelineLookRowDrawer
    {
        /// <summary>瞳回転スライダー (-1〜1) の両端に対応する回転角</summary>
        private const float EyeRotationMaxAngle = 90f;

        public const string HeadKeyDisabledMessage =
            "タイムライン設定の「顔/瞳の固定化」を有効にしてください";

        /// <summary>
        /// タイムライン視線の注視先の選択肢。
        /// モデル注視は StudioModelManager 未移植のため除外する (レイヤー側の扱いに合わせる)
        /// </summary>
        private static readonly List<MTEP.LookAtTargetType> LookAtTargetTypes =
            Enum.GetValues(typeof(MTEP.LookAtTargetType)).Cast<MTEP.LookAtTargetType>()
                .Where(type => type != MTEP.LookAtTargetType.Model).ToList();

        /// <summary>
        /// タイムライン視線がキー化される状態か。
        /// 参照するのは TimelineData 側のフラグ。
        /// タイムライン未読込 (timeline == null) も false を返すため、
        /// 「固定化が無効」と案内する呼び出し側は timeline が非 null であることを
        /// 事前に保証すること (未読込と固定化オフで案内文が混ざらないようにするため)
        /// </summary>
        public static bool IsHeadKeyEnabled
        {
            get
            {
                var timeline = MTEP.TimelineManager.instance.timeline;
                return timeline != null && timeline.useHeadKey;
            }
        }

        /// <summary>タイムライン視線の注視先コンボ。書き込み先は MaidCache</summary>
        private readonly GUIComboBox<MTEP.LookAtTargetType> _targetTypeComboBox =
            new GUIComboBox<MTEP.LookAtTargetType>
            {
                getName = (type, index) => MTEP.TransformDataLookAtTarget.TargetTypeNames[index],
            };

        private readonly GUIComboBox<MTEP.MaidCache> _targetMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            };

        private readonly GUIComboBox<MTEP.MaidPointType> _targetMaidPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = Enum.GetValues(typeof(MTEP.MaidPointType))
                    .Cast<MTEP.MaidPointType>().ToList(),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
            };

        /// <summary>注視先の行 (対象がメイドのときはメイド・ポイントの行も続けて出す)</summary>
        public void DrawLookAtTargetRows(
            GUIView view, MTEP.MaidCache maidCache, float labelWidth, float rowHeight)
        {
            // 選択肢から除外した Model が既存データに残っている場合は手動 (None) へ丸める
            var targetTypeIndex = (int) maidCache.lookAtTargetType;
            if (targetTypeIndex >= LookAtTargetTypes.Count)
            {
                targetTypeIndex = (int) MTEP.LookAtTargetType.None;
            }

            _targetTypeComboBox.items = LookAtTargetTypes;
            _targetTypeComboBox.currentIndex = targetTypeIndex;
            _targetTypeComboBox.onSelected =
                (type, _) => maidCache.lookAtTargetType = type;
            LabeledComboRow.Draw(view, "注視先", _targetTypeComboBox, labelWidth, rowHeight);

            if (_targetTypeComboBox.currentItem != MTEP.LookAtTargetType.Maid)
            {
                return;
            }

            _targetMaidComboBox.items = MTEP.MaidManager.instance.maidCaches;
            _targetMaidComboBox.currentIndex = maidCache.lookAtTargetIndex;
            _targetMaidComboBox.onSelected =
                (_, index) => maidCache.lookAtTargetIndex = index;
            LabeledComboRow.Draw(view, "メイド", _targetMaidComboBox, labelWidth, rowHeight);

            _targetMaidPointComboBox.currentIndex = (int) maidCache.lookAtMaidPointType;
            _targetMaidPointComboBox.onSelected =
                (type, _) => maidCache.lookAtMaidPointType = type;
            LabeledComboRow.Draw(view, "ポイント", _targetMaidPointComboBox, labelWidth, rowHeight);
        }

        /// <summary>瞳回転の 2 行。注視先が手動のときだけ効く (レイヤー側の活性条件と同じ)</summary>
        public void DrawEyeRotationRows(GUIView view, MTEP.MaidCache maidCache, float labelWidth)
        {
            // DrawSliderValue は内部でボタン等を描き、その EndEnabled が GUI.enabled を
            // 基準値へ戻してしまう。BeginEnabled は入れ子にできないため、
            // 基準値そのものを動かす SetEnabled で囲む
            view.SetEnabled(maidCache.lookAtTargetType == MTEP.LookAtTargetType.None);

            var eyeEulerAngle = maidCache.eyeEulerAngle;
            DrawEyeRotationSlider(view, "瞳回転左右", labelWidth,
                eyeEulerAngle.x / EyeRotationMaxAngle,
                value => maidCache.eyeEulerAngle =
                    new Vector3(value * EyeRotationMaxAngle, 0f, eyeEulerAngle.z));
            DrawEyeRotationSlider(view, "瞳回転上下", labelWidth,
                eyeEulerAngle.z / EyeRotationMaxAngle,
                value => maidCache.eyeEulerAngle =
                    new Vector3(eyeEulerAngle.x, 0f, value * EyeRotationMaxAngle));

            view.SetEnabled(true);
        }

        /// <summary>瞳回転スライダー 1 本。値域はレイヤー側に合わせて -1〜1</summary>
        private static void DrawEyeRotationSlider(
            GUIView view, string label, float labelWidth, float value, Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = onChanged,
            });
        }
    }
}
