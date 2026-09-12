using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サブカメラレイヤー (SubCameraTimelineLayer) のメニュー項目 → サブカメラの設定編集UI。
    /// 項目はサブカメラ 1 台につき 1 行で、セット行は無い。
    /// 逆方向: サブカメラに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class SubCameraItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>「追従ポイント」が収まる幅 (レイヤー UI と同じ)</summary>
        private const float LabelWidth = 70f;

        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;

        private readonly ItemRowDrawerCache<SubCameraRowDrawer> _cameraRowDrawers =
            new ItemRowDrawerCache<SubCameraRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集モード外はレイヤーが毎フレーム再生値を書き戻すため、
            // 値を書く直前に編集モードへ入る (BeginAutoEditMode)
            view.BeginAutoEditMode();

            foreach (var item in items)
            {
                var cameraData = subCameraManager.GetCamera(item.name);
                if (cameraData == null || cameraData.camera == null)
                {
                    // カメラ削除の直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (カメラが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのカメラの行か分かるよう見出しを出す
                view.DrawLabel(cameraData.displayName, -1, RowHeight);
                _cameraRowDrawers.Get(item.name).Draw(
                    view, cameraData, LabelWidth, RowHeight);
            }

            _cameraRowDrawers.PruneExcept(items);

            view.EndAutoEditMode();
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
