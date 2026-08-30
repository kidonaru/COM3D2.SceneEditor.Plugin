using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアルをキー化するレイヤー共通のプロバイダ。
    /// メニュー項目名からマテリアルを引ければ、マテリアルウィンドウと同じ行を出せる。
    /// マテリアル一覧の持ち主と追跡ストアだけが派生先で変わる
    /// </summary>
    public abstract class MaterialItemInspectorBase : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        /// <summary>メニュー項目名からマテリアルを引く。見つからなければ null</summary>
        protected abstract MTEP.ModelMaterial FindMaterial(
            MTEP.ITimelineLayer layer, string itemName);

        /// <summary>そのマテリアルの変更追跡先。追跡しない対象は既定値を返す</summary>
        protected abstract MaterialTrackTarget CreateTrack(
            MTEP.ITimelineLayer layer, MTEP.ModelMaterial material);

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                var material = FindMaterial(layer, item.name);
                if (material == null)
                {
                    // 着替え・モデル差し替えの直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (マテリアルが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                var track = CreateTrack(layer, material);
                // 追跡チェック行がマテリアル名を兼ねているため、追跡しない対象
                // (背景モデル) では名前が出ない。複数選択した行を見分けられるよう補う
                if (!track.isEnabled)
                {
                    view.DrawLabel(item.displayName, -1, RowHeight);
                }

                // 複数マテリアルを並べるため、色行のラベル (= ピッカーの同定キー) を
                // マテリアル名で一意にする
                MaterialPropertyRowsDrawer.Draw(
                    view, material, track, RowHeight, material.name);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            // マテリアルに対応する SelectionManager の選択概念が無いため逆方向同期はしない
            return null;
        }
    }
}
