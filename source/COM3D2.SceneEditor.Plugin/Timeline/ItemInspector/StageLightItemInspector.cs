using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ステージライトレイヤー (StageLightTimelineLayer) のメニュー項目 → 演出パラメータの編集UI。
    /// 項目はグループのセット行の下に、コントローラー (一括設定) 1 つとライトが並ぶ。
    /// 逆方向: ステージライトに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class StageLightItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.StageLightManager lightManager => MTEP.StageLightManager.instance;

        private readonly ItemRowDrawerCache<StageLightRowDrawer> _rowDrawers =
            new ItemRowDrawerCache<StageLightRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (ライブ演出ウィンドウと同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみステージライトを操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                _rowDrawers.PruneExcept(items);
                return;
            }

            foreach (var item in items)
            {
                // 複数選択時にどの対象の行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);

                var controller = lightManager.GetController(item.name);
                if (controller != null)
                {
                    _rowDrawers.Get(item.name)
                        .DrawControllerRows(view, controller, item.name);
                    continue;
                }

                var light = lightManager.GetLight(item.name);
                if (light == null || light.transform == null)
                {
                    // グループやライトを減らした直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel("(ライトが見つかりません)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                _rowDrawers.Get(item.name)
                    .DrawLightRows(view, light.controller, light, item.name);
            }

            _rowDrawers.PruneExcept(items);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
