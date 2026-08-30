using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ステージレーザーレイヤー (StageLaserTimelineLayer) のメニュー項目 → 演出パラメータの編集UI。
    /// 項目はグループのセット行の下に、コントローラー (一括設定) 1 つとレーザーが並ぶ。
    /// 逆方向: ステージレーザーに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class StageLaserItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.StageLaserManager laserManager => MTEP.StageLaserManager.instance;

        private readonly ItemRowDrawerCache<StageLaserRowDrawer> _rowDrawers =
            new ItemRowDrawerCache<StageLaserRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (ライブ演出ウィンドウと同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみステージレーザーを操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                _rowDrawers.PruneExcept(items);
                return;
            }

            foreach (var item in items)
            {
                // 複数選択時にどの対象の行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);

                var controller = laserManager.GetController(item.name);
                if (controller != null)
                {
                    _rowDrawers.Get(item.name)
                        .DrawControllerRows(view, controller, item.name);
                    continue;
                }

                var laser = laserManager.GetLaser(item.name);
                if (laser == null || laser.transform == null)
                {
                    // グループやレーザーを減らした直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel("(レーザーが見つかりません)", -1, RowHeight,
                        textColor: Color.gray);
                    continue;
                }

                _rowDrawers.Get(item.name)
                    .DrawLaserRows(view, laser.controller, laser, item.name);
            }

            _rowDrawers.PruneExcept(items);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
