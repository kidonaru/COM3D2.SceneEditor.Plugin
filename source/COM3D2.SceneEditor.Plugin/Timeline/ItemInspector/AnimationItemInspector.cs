using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// アニメレイヤー (AnimationTimelineLayer) のメニュー項目 → アニメブレンド設定の編集UI。
    /// 項目はアニメレイヤー 1 段につき 1 行 ("Animation2" のように末尾が段番号)。
    ///
    /// 他のレイヤー固有プロバイダは同じ意味の編集をこちら側へ書き起こすが、
    /// 本クラスは例外的にレイヤーの DrawAnimeLayer (public) をそのまま呼ぶ。
    /// 編集後の反映は private な再生データ (_playDataMap) を参照して再生位置を計算するため、
    /// その処理をプロバイダ側で再現できないため。
    /// 逆方向: アニメレイヤーに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class AnimationItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var animationLayer = layer as MTEP.AnimationTimelineLayer;
            if (animationLayer == null || animationLayer.maid == null)
            {
                view.DrawLabel("対象メイドが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            foreach (var item in items)
            {
                int layerIndex;
                if (!MTEP.AnimationTimelineLayer.AnimationLayerNameMap
                        .TryGetValue(item.name, out layerIndex))
                {
                    // 段数の異なるデータを読み込んだ場合などにここへ来る
                    view.DrawLabel(item.displayName + " (アニメレイヤーが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 段名の見出しは DrawAnimeLayer が自前で描くため、ここでは足さない
                animationLayer.DrawAnimeLayer(view, layerIndex);
            }

            // DrawAnimeLayer は編集モード判定で無効化したまま戻すため、ここで戻す
            view.SetEnabled(view.focusedComboBox == null);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
