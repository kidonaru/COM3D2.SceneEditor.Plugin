using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 脱衣レイヤー (UndressTimelineLayer) のメニュー項目 → スロット表示トグル。
    ///
    /// 脱衣ウィンドウの行は nei 由来のカテゴリ (複数の TBody.SlotID をまとめたもの) 単位で、
    /// レイヤーがキー化する DressSlotID 単位の行が存在しない。
    /// そのため行描画は共有せず、レイヤーと同じ書き込み経路 (DressUtils.SetSlotVisible) で直接描く。
    /// これにより Inspector で編集した状態がそのままキーフレームに載る
    /// </summary>
    public class UndressItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>MaidUndressWindow.TOGGLE_WIDTH (115) と同値 (脱衣ウィンドウと見た目を揃える)</summary>
        private const float ToggleWidth = 115f;

        /// <summary>
        /// メニュー項目名 (DressSlotID の名前) をスロット ID へ戻す。
        /// DressUtils.GetDressSlotId は未知の名前を wear へ丸めてしまい、
        /// 別スロットを書き換える事故になるためここでは使わない
        /// </summary>
        public static bool TryResolveSlotId(string itemName, out MTEP.DressSlotID slotId)
        {
            slotId = MTEP.DressSlotID.wear;
            if (string.IsNullOrEmpty(itemName))
            {
                return false;
            }

            // Enum.Parse は数値文字列も通してしまうため、定義済みの名前であることまで確かめる
            // (ここを通れば Parse は失敗しないので例外処理は要らない)
            if (!Enum.IsDefined(typeof(MTEP.DressSlotID), itemName))
            {
                return false;
            }

            slotId = (MTEP.DressSlotID) Enum.Parse(typeof(MTEP.DressSlotID), itemName);
            return true;
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

            foreach (var item in items)
            {
                MTEP.DressSlotID slotId;
                if (!TryResolveSlotId(item.name, out slotId))
                {
                    // 現状のメニュー項目名は必ず DressSlotID 名なので通らないが、
                    // レイヤー側が項目を増やしたときに誤ったスロットを触らないための保険
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                // トグル ON = 表示中。キーフレームの isVisible に合わせているため、
                // 脱衣ウィンドウのカテゴリ行 (ON = 脱衣中) とは極性が逆になる。
                // 未装着スロットもウィンドウのように false へ倒さず実値をそのまま出す
                // (キーフレームに載る値と表示を一致させるため)。
                // ただし切り替えても見た目が変わらないため操作はさせない
                // (無効化の判断は脱衣ウィンドウのカテゴリ行と同じ流儀)
                var enabled = MTEP.DressUtils.IsSlotLoaded(maidCache, slotId);
                var displayName = item.displayName;

                view.DrawToggle(displayName, MTEP.DressUtils.IsSlotVisible(maidCache, slotId),
                    ToggleWidth, RowHeight, enabled,
                    value =>
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Undress,
                            "脱衣: " + displayName);
                        MTEP.DressUtils.SetSlotVisible(maidCache, slotId, value);
                    });
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 脱衣スロットに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
