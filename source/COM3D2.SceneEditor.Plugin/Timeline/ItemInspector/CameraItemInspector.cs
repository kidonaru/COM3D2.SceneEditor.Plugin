using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラレイヤー (CameraTimelineLayer) のメニュー項目 → メインカメラの構図編集UI。
    /// キー化されるのは追従設定・注視点・回転・距離・FOV で、「カメラ」項目では
    /// CameraWindow と同じ行を出す。「手ブレ」項目だけを選んだときは揺れパラメータの行に切り替える。
    /// 逆方向: カメラに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class CameraItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>CameraWindow の LABEL_WIDTH と同じ値 (「注視点」が収まる幅)</summary>
        private const float LabelWidth = 70f;

        private readonly MaidFollowRowDrawer _followRowDrawer = new MaidFollowRowDrawer();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 手ブレ項目だけが選ばれているときは揺れパラメータの行を出す
            if (items.Count > 0 && IsShakeOnly(items))
            {
                CameraShakeRowDrawer.Draw(view, RowHeight);
                return;
            }

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

            var follow = MTEP.MaidFollowMainCamera.instance;
            if (follow != null)
            {
                _followRowDrawer.Draw(view, follow.state, LabelWidth, RowHeight);
                view.DrawHorizontalLine();
            }

            MainCameraRowDrawer.DrawTargetPosRow(view, mainCamera, follow, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawAngleSliders(view, mainCamera, camera, follow, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawDistanceFovSliders(
                view, mainCamera, camera, LabelWidth, RowHeight);
        }

        private static bool IsShakeOnly(IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                if (item.name != MTEP.CameraTimelineLayer.ShakeBoneName)
                {
                    return false;
                }
            }
            return true;
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
