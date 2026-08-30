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
            /// <summary>瞳の位置・サイズ (EyesPos*/EyesSca*)</summary>
            EyesPos,
            /// <summary>編集UIを持たない項目</summary>
            Unsupported,
        }

        private const float RowHeight = 20f;
        /// <summary>MaidWindowBase.LABEL_WIDTH (70) と同値 (表情ウィンドウと見た目を揃える)</summary>
        private const float LabelWidth = 70f;

        private readonly TimelineLookRowDrawer _lookRowDrawer = new TimelineLookRowDrawer();
        private readonly EyesPosRowDrawer _eyesPosRowDrawer = new EyesPosRowDrawer();

        /// <summary>メニュー項目名から編集UIの種別を求める</summary>
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
                    return EyesPosRowDrawer.IsEyesPosType(eyesType)
                        ? RowKind.EyesPos : RowKind.Unsupported;
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

            // 固定化が無効だと視線の編集はキー化されないため、表情ウィンドウと同じ案内を出す。
            // 瞳の位置・サイズは固定化に依らずキー化されるため、この案内の対象外
            var isHeadKeyEnabled = TimelineLookRowDrawer.IsHeadKeyEnabled;

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

                if (rowKind == RowKind.EyesPos)
                {
                    var eyesType = MTEP.EyesTimelineLayer.EyesTypeMap[item.name];
                    _eyesPosRowDrawer.DrawEyesSliderRows(view, maidCache, eyesType);
                    continue;
                }

                if (!isHeadKeyEnabled)
                {
                    view.DrawLabel(TimelineLookRowDrawer.HeadKeyDisabledMessage,
                        -1, RowHeight, textColor: Color.yellow);
                    continue;
                }

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
