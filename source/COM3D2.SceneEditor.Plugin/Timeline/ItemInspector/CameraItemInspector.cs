using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラレイヤー (CameraTimelineLayer) のメニュー項目 → メインカメラの構図編集UI。
    /// キー化されるのは注視点・回転・距離・FOV で、項目は「カメラ」1 つだけのため
    /// 選択内容によらず CameraWindow と同じ行を出す。
    /// 逆方向: カメラに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class CameraItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>CameraWindow の LABEL_WIDTH と同じ値 (「注視点」が収まる幅)</summary>
        private const float LabelWidth = 70f;

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // ウィンドウと違い項目選択中は毎フレーム呼ばれるため、
            // シーン遷移で GameMain が居ない瞬間も NRE にならないようにする
            var gameMain = GameMain.Instance;
            var mainCamera = gameMain != null ? gameMain.MainCamera : null;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (mainCamera == null || camera == null)
            {
                view.DrawLabel("メインカメラが見つかりません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            MainCameraRowDrawer.DrawTargetPosRow(view, mainCamera, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawAngleSliders(view, mainCamera, camera, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawDistanceFovSliders(
                view, mainCamera, camera, LabelWidth, RowHeight);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
