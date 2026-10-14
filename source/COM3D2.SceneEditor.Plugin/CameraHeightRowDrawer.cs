using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の設定行 (基準メイド・そのスロットの基準身長)。編集先は CameraHeightManager のライブ値で、
    /// タイムラインを読み込んでいなくても編集できる。編集モード中に変えると、他の項目と同じく差分として身長補正のキーになる。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class CameraHeightRowDrawer
    {
        private const float ButtonWidth = 60f;
        private const float FieldWidth = 50f;
        // 手入力の途中値で倍率が極端にならないよう、式の取りうる範囲 (134〜199cm) より広めに絞る
        private const int MinBaseHeight = 100;
        private const int MaxBaseHeight = 250;

        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static CameraHeightManager heightManager => CameraHeightManager.instance;
        private static MTEP.CameraHeightState heightState => heightManager.state;
        private static MTEP.CameraHeightBases bases => heightManager.bases;

        /// <summary>先頭に「なし」(null) を含む基準メイドの選択肢</summary>
        private readonly List<MTEP.MaidCache> _maidItems = new List<MTEP.MaidCache>();

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = MaidFollowRowDrawer.GetFollowMaidName,
        };

        private static void SetBaseHeight(int slotNo, int height)
        {
            // RecordCameraEdit にすると、基準身長を Undo の対象外にした方針 (全再構築で古い値へ戻る) に反する
            AutoEditMode.Enter();
            bases.Set(slotNo, height);
        }

        public void Draw(GUIView view, float labelWidth, float rowHeight)
        {
            MaidFollowRowDrawer.FillFollowMaidItems(_maidItems);
            _maidComboBox.items = _maidItems;
            _maidComboBox.currentIndex =
                MaidFollowRowDrawer.ToFollowMaidIndex(heightState.liveSlotNo, _maidItems.Count);
            _maidComboBox.onSelected = (maidCache, index) =>
            {
                MainCameraRowDrawer.RecordCameraEdit("身長補正");
                var selectedSlotNo = MaidFollowRowDrawer.ToFollowMaidSlotNo(index);
                heightState.SetLive(selectedSlotNo);
                if (selectedSlotNo >= 0)
                {
                    bases.SetIfUnset(selectedSlotNo, CameraHeightManager.GetSlotHeight(selectedSlotNo));
                }
            };
            LabeledComboRow.Draw(view, "身長補正", _maidComboBox, labelWidth, rowHeight);

            var slotNo = heightState.liveSlotNo;
            if (slotNo < 0)
            {
                return;
            }

            // コンボは範囲外のスロットを末尾のメイドへ寄せて表示するため、補正が無効なことをここで明示する
            if (maidManager.GetMaidCache(slotNo) == null)
            {
                view.DrawLabel(string.Format("基準メイドが見つかりません (スロット {0})。補正は無効です",
                    slotNo), -1, rowHeight, textColor: Color.yellow);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = "基準身長",
                    labelWidth = labelWidth,
                    value = bases.Get(slotNo),
                    minValue = MinBaseHeight,
                    maxValue = MaxBaseHeight,
                    fieldWidth = FieldWidth,
                    height = rowHeight,
                    onChanged = x => SetBaseHeight(slotNo, x),
                });

                if (view.DrawButton("取得", ButtonWidth, rowHeight))
                {
                    SetBaseHeight(slotNo, CameraHeightManager.GetSlotHeight(slotNo));
                }
            }
            view.EndLayout();

            // 選択以外の経路 (キーの値の書き換え・貼り付け) で基準メイドになったスロットは基準身長が未設定のまま残り、x1.00 だけでは気づけない
            if (bases.Get(slotNo) <= 0)
            {
                view.DrawLabel("基準身長が未設定のため補正は無効です (取得を押してください)",
                    -1, rowHeight, textColor: Color.yellow);
            }

            float rootY;
            var scale = heightManager.GetSlotScale(slotNo, out rootY);
            view.DrawLabel(string.Format("現在 {0}cm / 補正 x{1:F2}",
                CameraHeightManager.GetSlotHeight(slotNo), scale), -1, rowHeight);
        }
    }
}
