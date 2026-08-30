using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン視線 (注視先 / 顔向きキー) の行描画。
    /// MaidFaceWindow の視線タブと TimelineItemInspector (瞳レイヤーの項目表示) で共有する。
    /// 書き込み先はどのスナップショットにも含まれない MaidCache のため履歴は記録しない。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class TimelineLookRowDrawer
    {
        public const string HeadKeyDisabledMessage =
            "表情ウィンドウの視線タブで「視線をキー化」を有効にしてください";

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

        /// <summary>キー化中に選べる向け先。毎フレーム複製しないよう控えておく</summary>
        private static readonly List<MaidLookMode> KeyedLookModes =
            MaidLookBridge.GetSelectableModes(true);

        /// <summary>
        /// キー化中の向け先コンボ。SE の向け先と同じ語彙 (MaidLookMode) を使い、
        /// 書き込み先だけが MaidCache のキー指定値になる
        /// </summary>
        private readonly GUIComboBox<MaidLookMode> _lookModeComboBox =
            new GUIComboBox<MaidLookMode>
            {
                getName = (mode, _) => mode.ToString(),
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

        /// <summary>キー化中のモデル注視の対象。番号 (modelNames の添字) で書き込む</summary>
        private readonly GUIComboBox<MTEP.StudioModelStat> _targetModelComboBox =
            new GUIComboBox<MTEP.StudioModelStat>
            {
                getName = (model, _) => model.displayName,
            };

        /// <summary>
        /// キー化中の向け先の行 (対象がメイドのときはメイド・ポイントの行も続けて出す)。
        /// キー化していないときの SE の「向け先」行と同じ語彙・同じラベルにして、
        /// キー化の切り替えで行が入れ替わらないようにする
        /// </summary>
        public void DrawLookAtTargetRows(
            GUIView view, MTEP.MaidCache maidCache, float labelWidth, float rowHeight)
        {
            // 選択肢に無い値 (無し・マウス・オブジェクト) は ToLookMode が方向指定へ丸める
            var mode = MaidLookBridge.ToLookMode(maidCache.lookAtTargetType);

            _lookModeComboBox.items = KeyedLookModes;
            _lookModeComboBox.currentIndex = KeyedLookModes.IndexOf(mode);
            _lookModeComboBox.onSelected =
                (newMode, _) => maidCache.lookAtTargetType = MaidLookBridge.ToTargetType(newMode);
            LabeledComboRow.Draw(view, "向け先", _lookModeComboBox, labelWidth, rowHeight);

            if (_lookModeComboBox.currentItem == MaidLookMode.モデル)
            {
                var models = MTEP.StudioModelManager.instance.models;
                _targetModelComboBox.items = models;
                // 番号は modelNames の添字。models と同順で構築されるため添字をそのまま使う
                var modelIndex = maidCache.lookAtTargetIndex >= 0
                    && maidCache.lookAtTargetIndex < models.Count
                    ? maidCache.lookAtTargetIndex : -1;
                _targetModelComboBox.defaultName = modelIndex >= 0 ? null : "未選択";
                _targetModelComboBox.currentIndex = modelIndex;
                _targetModelComboBox.onSelected =
                    (_, index) => maidCache.lookAtTargetIndex = index;
                LabeledComboRow.Draw(view, "モデル", _targetModelComboBox, labelWidth, rowHeight);
                return;
            }

            if (_lookModeComboBox.currentItem != MaidLookMode.メイド)
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

        /// <summary>顔向きキーの 2 行。向け先が方向指定のときだけ効く (レイヤー側の活性条件と同じ)</summary>
        public void DrawLookDirectionRows(GUIView view, MTEP.MaidCache maidCache, float labelWidth)
        {
            // DrawSliderValue は内部でボタン等を描き、その EndEnabled が GUI.enabled を
            // 基準値へ戻してしまう。BeginEnabled は入れ子にできないため、
            // 基準値そのものを動かす SetEnabled で囲む
            view.SetEnabled(maidCache.lookAtTargetType == MTEP.LookAtTargetType.None);

            var lookDirection = maidCache.lookDirection;
            DrawLookDirectionSlider(view, "顔向き左右", labelWidth,
                lookDirection.x,
                value => maidCache.lookDirection = new Vector2(value, lookDirection.y));
            DrawLookDirectionSlider(view, "顔向き上下", labelWidth,
                lookDirection.y,
                value => maidCache.lookDirection = new Vector2(lookDirection.x, value));

            view.SetEnabled(true);
        }

        /// <summary>顔向きスライダー 1 本。値域は SE の顔向きに合わせて -1〜1</summary>
        private static void DrawLookDirectionSlider(
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
