using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サイリウムレイヤー (PsylliumTimelineLayer) のメニュー項目 → 演出パラメータの編集UI。
    /// 項目はグループのセット行の下にコントローラー・バー設定・持ち手設定が並び、
    /// 別のセット行としてエリア・パターン・移動回転がある。
    /// パターンと移動回転はウィンドウ側が両手/右手/左手のタブで対象を切り替える構造のため、
    /// Inspector には出さずライブ演出ウィンドウへ誘導する。
    /// 逆方向: サイリウムに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class PsylliumItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.PsylliumManager psylliumManager => MTEP.PsylliumManager.instance;

        private readonly ItemRowDrawerCache<PsylliumRowDrawer> _rowDrawers =
            new ItemRowDrawerCache<PsylliumRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (ライブ演出ウィンドウと同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみサイリウムを操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                _rowDrawers.PruneExcept(items);
                return;
            }

            foreach (var item in items)
            {
                // 複数選択時にどの対象の行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);
                DrawItem(view, item);
            }

            _rowDrawers.PruneExcept(items);
        }

        private void DrawItem(GUIView view, MTEP.IBoneMenuItem item)
        {
            var drawer = _rowDrawers.Get(item.name);

            var controller = psylliumManager.GetController(item.name);
            if (controller != null)
            {
                drawer.DrawControllerRows(view, controller);
                return;
            }

            // バー設定・持ち手設定はコントローラーが所有する。
            // 設定側からコントローラーを辿れないため、所有者を探して渡す
            foreach (var owner in psylliumManager.controllers)
            {
                if (owner.barConfig != null && owner.barConfig.name == item.name)
                {
                    drawer.DrawBarConfigRows(view, owner, item.name);
                    return;
                }

                if (owner.handConfig != null && owner.handConfig.name == item.name)
                {
                    drawer.DrawHandConfigRows(view, owner);
                    return;
                }
            }

            var area = psylliumManager.GetArea(item.name);
            if (area != null)
            {
                drawer.DrawAreaRows(view, area);
                return;
            }

            // パターン・移動回転、およびグループを減らした直後の残存キーはここに来る
            view.DrawLabel("(この項目はライブ演出ウィンドウで編集してください)", -1, RowHeight,
                textColor: Color.gray);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
