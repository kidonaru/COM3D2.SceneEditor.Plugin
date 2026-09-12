using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド追従の設定行 (追従メイド / 追従ポイント / 向き反映)。
    /// サブカメラ・メインカメラ・SceneView カメラで共用する。
    /// 履歴の記録は呼び出し側の責務で、書き込み直前に onBeforeChange が呼ばれる。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class MaidFollowRowDrawer
    {
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;

        private readonly GUIComboBox<MTEP.MaidCache> _followMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "なし" : maidCache.fullName,
            };

        private readonly GUIComboBox<MTEP.MaidPointType> _followPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = Enum.GetValues(typeof(MTEP.MaidPointType))
                    .Cast<MTEP.MaidPointType>().ToList(),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
            };

        /// <summary>先頭に「なし」(null) を含む追従メイドの選択肢</summary>
        private readonly List<MTEP.MaidCache> _followMaidItems = new List<MTEP.MaidCache>();

        /// <summary>追従中の追従ポイントのコンボが 1 行の幅に占める割合</summary>
        private const float FollowPointComboWidthRatio = 0.4f;

        /// <summary>追従メイド行 (追従中は同じ行に追従ポイント) と、追従中のみ向き反映トグルを描く</summary>
        /// <param name="onBeforeChange">
        /// 値を書き込む直前に呼ばれる。履歴を記録しない呼び出し側は省略してよい
        /// </param>
        public void Draw(GUIView view, MTEP.MaidFollowState follow, float labelWidth, float rowHeight,
            Action onBeforeChange = null)
        {
            DrawFollowRow(view, follow, labelWidth, rowHeight, onBeforeChange);

            if (!follow.isFollow)
            {
                return;
            }

            view.DrawToggle("向き反映", follow.followRotation, 100, rowHeight,
                newValue =>
                {
                    onBeforeChange?.Invoke();
                    follow.followRotation = newValue;
                });
        }

        /// <summary>
        /// 追従メイドの選択行。先頭の「なし」を選ぶと追従を解除する。
        /// 追従中は同じ行の右側に追従ポイントのコンボを並べる (ラベルは省略)。
        /// 2 つ並べるとコンボが潰れる細いビューでは、従来どおり追従ポイントを次の行へ落とす
        /// </summary>
        private void DrawFollowRow(
            GUIView view, MTEP.MaidFollowState follow, float labelWidth, float rowHeight,
            Action onBeforeChange)
        {
            _followMaidItems.Clear();
            _followMaidItems.Add(null);
            _followMaidItems.AddRange(maidManager.maidCaches);

            _followMaidComboBox.items = _followMaidItems;
            _followMaidComboBox.currentIndex =
                ToFollowMaidIndex(follow.maidSlotNo, _followMaidItems.Count);
            _followMaidComboBox.onSelected = (maidCache, index) =>
            {
                onBeforeChange?.Invoke();
                follow.maidSlotNo = ToFollowMaidSlotNo(index);
            };

            if (!follow.isFollow)
            {
                LabeledComboRow.Draw(
                    view, "追従メイド", _followMaidComboBox, labelWidth, rowHeight);
                return;
            }

            _followPointComboBox.currentIndex = (int)follow.maidPointType;
            _followPointComboBox.onSelected = (type, _) =>
            {
                onBeforeChange?.Invoke();
                follow.maidPointType = type;
            };

            if (!LabeledComboRow.CanFitCombos(view, labelWidth, 2))
            {
                LabeledComboRow.Draw(
                    view, "追従メイド", _followMaidComboBox, labelWidth, rowHeight);
                LabeledComboRow.Draw(
                    view, "追従ポイント", _followPointComboBox, labelWidth, rowHeight);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("追従メイド", labelWidth, rowHeight, style: GUIView.gsLabelRight);

                var totalWidth = LabeledComboRow.CalcComboWidth(view, labelWidth, 2);
                var pointWidth = totalWidth * FollowPointComboWidthRatio;

                LabeledComboRow.DrawCombo(
                    view, _followMaidComboBox, totalWidth - pointWidth, rowHeight);
                LabeledComboRow.DrawCombo(
                    view, _followPointComboBox, pointWidth, rowHeight);
            }
            view.EndLayout();
        }

        /// <summary>
        /// 追従メイドの maidSlotNo → コンボの添字。先頭に「なし」がある分 1 つずれる。
        /// 退去などで選択肢が減ったスロット番号は末尾へ丸める
        /// </summary>
        public static int ToFollowMaidIndex(int maidSlotNo, int itemCount)
        {
            return Mathf.Clamp(maidSlotNo + 1, 0, itemCount - 1);
        }

        /// <summary>コンボの添字 → 追従メイドの maidSlotNo (「なし」は -1)</summary>
        public static int ToFollowMaidSlotNo(int index)
        {
            return index - 1;
        }
    }
}
