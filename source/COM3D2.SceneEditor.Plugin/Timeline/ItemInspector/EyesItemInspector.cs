using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>瞳レイヤー (EyesTimelineLayer) のメニュー項目 → タイムライン視線編集UI</summary>
    public class EyesItemInspector : ITimelineItemInspector
    {
        /// <summary>メニュー項目に対応する編集UIの種別</summary>
        public enum RowKind
        {
            /// <summary>顔向き (EyesRot。旧瞳回転キーを顔向きとして解釈する)</summary>
            LookDirection,
            /// <summary>注視先 (LookAtTarget)</summary>
            LookAtTarget,
            /// <summary>編集UIを持たない項目 (瞳の位置・サイズ)</summary>
            Unsupported,
        }

        private const float RowHeight = 20f;
        /// <summary>MaidWindowBase.LABEL_WIDTH (70) と同値 (表情ウィンドウと見た目を揃える)</summary>
        private const float LabelWidth = 70f;

        private readonly TimelineLookRowDrawer _lookRowDrawer = new TimelineLookRowDrawer();

        /// <summary>
        /// メニュー項目名から編集UIの種別を求める。
        /// 瞳の位置・サイズ (EyesPos*/EyesSca*) は対応する編集UIがどのウィンドウにも無く、
        /// スライダーの値域も決められないため未対応として扱う
        /// </summary>
        public static RowKind ResolveRowKind(string itemName)
        {
            MTEP.MotionEyesType eyesType;
            if (!MTEP.EyesTimelineLayer.EyesTypeMap.TryGetValue(itemName, out eyesType))
            {
                return RowKind.Unsupported;
            }

            switch (eyesType)
            {
                case MTEP.MotionEyesType.EyesRot:
                    return RowKind.LookDirection;
                case MTEP.MotionEyesType.LookAtTarget:
                    return RowKind.LookAtTarget;
                default:
                    return RowKind.Unsupported;
            }
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var maid = layer.maid;
            var maidCache = maid != null
                ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            if (maidCache == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            // 固定化が無効だと編集してもキー化されないため、表情ウィンドウと同じ案内を出す。
            // タイムライン未読込の場合は TimelineItemInspector.ShouldDraw が
            // ここへ到達させないため、この案内は固定化オフのときだけ出る
            if (!TimelineLookRowDrawer.IsHeadKeyEnabled)
            {
                view.DrawLabel(TimelineLookRowDrawer.HeadKeyDisabledMessage,
                    -1, RowHeight, textColor: Color.yellow);
                return;
            }

            foreach (var item in items)
            {
                var rowKind = ResolveRowKind(item.name);
                if (rowKind == RowKind.Unsupported)
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの項目の行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);

                if (rowKind == RowKind.LookDirection)
                {
                    _lookRowDrawer.DrawLookDirectionRows(view, maidCache, LabelWidth);
                }
                else
                {
                    _lookRowDrawer.DrawLookAtTargetRows(view, maidCache, LabelWidth, RowHeight);
                }
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 瞳の設定に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
