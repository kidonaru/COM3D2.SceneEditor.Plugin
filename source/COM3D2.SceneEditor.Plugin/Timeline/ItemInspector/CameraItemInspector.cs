using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラレイヤー (CameraTimelineLayer) のメニュー項目 → メインカメラの構図編集UI。
    /// キー化されるのは追従設定・注視点・回転・距離・FOV・身長補正の基準メイドで、「カメラ」項目では
    /// CameraWindow と同じ行を出す。「手ブレ」「身長補正」の項目だけを選んだときは、その設定行に切り替える。
    /// 逆方向: カメラに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class CameraItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>CameraWindow の LABEL_WIDTH と同じ値 (「注視点」が収まる幅)</summary>
        private const float LabelWidth = 70f;

        private readonly MaidFollowRowDrawer _followRowDrawer = new MaidFollowRowDrawer();
        private readonly CameraHeightRowDrawer _heightRowDrawer = new CameraHeightRowDrawer();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 手ブレ・身長補正の項目だけが選ばれているときは、その設定行だけを出す
            if (IsOnly(items, MTEP.CameraTimelineLayer.ShakeBoneName))
            {
                CameraShakeRowDrawer.Draw(view, RowHeight);
                return;
            }
            if (IsOnly(items, MTEP.CameraTimelineLayer.HeightBoneName))
            {
                _heightRowDrawer.Draw(view, LabelWidth, RowHeight);
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
                _followRowDrawer.Draw(view, follow.state, LabelWidth, RowHeight,
                    () => MainCameraRowDrawer.RecordCameraEdit("追従"));
                view.DrawHorizontalLine();
            }

            _heightRowDrawer.Draw(view, LabelWidth, RowHeight);
            view.DrawHorizontalLine();

            MainCameraRowDrawer.DrawTargetPosRow(view, mainCamera, follow, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawAngleSliders(view, mainCamera, camera, follow, LabelWidth, RowHeight);
            view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawDistanceFovSliders(
                view, mainCamera, camera, LabelWidth, RowHeight);
        }

        private static bool IsOnly(IList<MTEP.IBoneMenuItem> items, string boneName)
        {
            if (items.Count == 0)
            {
                return false;
            }
            foreach (var item in items)
            {
                if (item.name != boneName)
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
