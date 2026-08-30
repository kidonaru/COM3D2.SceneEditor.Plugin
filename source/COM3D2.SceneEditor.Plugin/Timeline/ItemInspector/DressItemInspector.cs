using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 衣装レイヤー (DressTimelineLayer) のメニュー項目 → 現在の装備の簡易表示。
    ///
    /// 衣装は「初期値からの差分」をキー化する仕組みで、値の編集はアイテム選択UI
    /// (ModItemExplorer 等) の領分になるため、Inspector では現在値の表示だけを行う。
    /// 表示形式はレイヤー自身のウィンドウ (DressTimelineLayer.DrawDress) に合わせている
    /// (レイヤー本体は MTE 逐語コピーで手を入れられないため共有はできず、同じ体裁を再現している)。
    /// ただし対象外の部位は、一覧を流すレイヤー側と違って明示的な理由ラベルを出す
    /// (項目を選んだのに何も出ないと壊れて見えるため)
    /// </summary>
    public class DressItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>
        /// メニュー項目名を部位へ戻す。
        /// MaidPartUtils.ToMaidPartType は未知の名前に null_mpn を返すため、
        /// 別部位を読みに行かないようここで弾く。
        /// 大文字小文字は区別しない (レイヤーが項目名を引くときと同じ辞書を使うため)
        /// </summary>
        public static bool TryResolvePartType(string itemName, out MaidPartType partType)
        {
            partType = MaidPartType.null_mpn;
            if (string.IsNullOrEmpty(itemName))
            {
                return false;
            }

            partType = MaidPartUtils.ToMaidPartType(itemName);
            return partType != MaidPartType.null_mpn;
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

            // 衣装の初期化・初期値更新は旧レイヤー編集ウィンドウから移設。
            // 部位単位ではなくメイド全体への操作のため項目の外に置く
            view.BeginHorizontal();
            {
                if (view.DrawButton("初期化", 60, RowHeight))
                {
                    maidCache.maidPropCache.ApplyInitialProp();
                }

                if (view.DrawButton("初期値更新", 100, RowHeight))
                {
                    maidCache.maidPropCache.UpdateInitialProp();
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine();

            foreach (var item in items)
            {
                MaidPartType partType;
                if (!TryResolvePartType(item.name, out partType))
                {
                    view.DrawLabel(item.displayName + " (未対応)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                var prop = maid.GetProp(MaidPartUtils.ToMPN(partType));
                if (prop == null)
                {
                    view.DrawLabel(item.displayName + " (この部位はありません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 初期値と違う部位は緑。どこがキー化されるかを一目で分かるようにする
                // (レイヤーのウィンドウと同じ色分け)
                var initialPropInfo = maidCache.maidPropCache.GetInitialPropInfo(partType);
                var color = prop.strFileName == initialPropInfo.propName
                    ? Color.white : Color.green;

                view.DrawLabel(item.displayName + ": " + prop.strFileName, -1, RowHeight, color);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // 装備部位に対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
