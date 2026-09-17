using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サブカメラ 1 台分の設定行 (有効 / 追従 / 位置・回転 / FoV / ビューポート)。
    ///
    /// サブカメラには委譲先の個別ウィンドウが無いため、編集 UI はここが受け持つ
    /// (書き込み先は SubCameraData / MaidFollowSubCamera)。
    ///
    /// コンボボックスの開閉状態と回転オフセットのキャッシュを持つため、
    /// カメラごと・描画するビューごとにインスタンスを分ける
    /// </summary>
    public class SubCameraRowDrawer
    {
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;

        /// <summary>レイヤー UI の FoV スライダーと同じ既定値</summary>
        private const float DefaultFov = 35f;

        private readonly MaidFollowRowDrawer _followRowDrawer = new MaidFollowRowDrawer();

        private readonly EulerOffsetCache _offsetCache = new EulerOffsetCache();

        public void Draw(
            GUIView view,
            MTEP.SubCameraData cameraData,
            float labelWidth,
            float rowHeight)
        {
            var camera = cameraData.camera;
            var follow = cameraData.follow;

            // 値を書き込む直前に呼ぶ。サブカメラ全台を 1 スナップショットで持つため対象キーは不要
            Action<string> recordEdit = label => HistoryManager.instance.BeforeEdit(
                null, HistoryScope.SubCamera, "サブカメラ: " + cameraData.name + " " + label,
                null, () => SubCameraSnapshot.Capture());

            view.DrawToggle("有効", cameraData.visible, 100, rowHeight,
                newValue =>
                {
                    recordEdit("有効");
                    cameraData.visible = newValue;
                });

            _followRowDrawer.Draw(view, follow.state, labelWidth, rowHeight,
                () => recordEdit("追従"));

            // 追従中の位置は追従点からのオフセットになる (SubCameraData.position と同じ扱い)
            Vector3RowDrawer.Draw(view,
                follow.isFollow ? "オフセット" : "位置",
                ObjectTransformRowDrawer.PositionSensitivity, labelWidth, rowHeight,
                cameraData.position,
                value =>
                {
                    recordEdit("位置");
                    cameraData.position = value;
                },
                () =>
                {
                    recordEdit("位置");
                    cameraData.position = Vector3.zero;
                });

            DrawRotationRow(view, cameraData, follow, labelWidth, rowHeight, recordEdit);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "FoV",
                labelWidth = labelWidth,
                width = -1,
                min = 1,
                max = 179,
                step = 0.1f,
                defaultValue = DefaultFov,
                value = camera.fieldOfView,
                onChanged = value =>
                {
                    recordEdit("FoV");
                    camera.fieldOfView = value;
                },
            });

            DrawViewportRows(view, cameraData, labelWidth, rowHeight, recordEdit);
        }

        /// <summary>
        /// 回転行。向き反映中は追従点からの角度オフセットをそのまま編集し、
        /// それ以外はカメラのワールド回転を編集する (SubCameraData.rotation と同じ扱い)。
        /// ワールド回転側は quaternion からの再分解で表示が飛ばないようキャッシュを通す
        /// </summary>
        private void DrawRotationRow(
            GUIView view, MTEP.SubCameraData cameraData, MTEP.MaidFollowSubCamera follow,
            float labelWidth, float rowHeight, Action<string> recordEdit)
        {
            if (follow.isFollow && follow.followRotation)
            {
                Vector3RowDrawer.Draw(view, "回転",
                    ObjectTransformRowDrawer.RotationSensitivity, labelWidth, rowHeight,
                    follow.eulerAnglesOffset,
                    value =>
                    {
                        recordEdit("回転");
                        follow.eulerAnglesOffset = value;
                    },
                    () =>
                    {
                        recordEdit("回転");
                        follow.eulerAnglesOffset = Vector3.zero;
                    });
                return;
            }

            var cameraTransform = cameraData.camera.transform;
            Vector3RowDrawer.Draw(view, "回転",
                ObjectTransformRowDrawer.RotationSensitivity, labelWidth, rowHeight,
                _offsetCache.GetOffset(cameraTransform, Quaternion.identity, false),
                value =>
                {
                    recordEdit("回転");
                    SetWorldEulerAngles(cameraTransform, value);
                },
                () =>
                {
                    recordEdit("回転");
                    SetWorldEulerAngles(cameraTransform, Vector3.zero);
                });
        }

        /// <summary>ワールド回転を書き込み、表示に使ったオイラー表現をキャッシュへ控える</summary>
        private void SetWorldEulerAngles(Transform cameraTransform, Vector3 eulerAngles)
        {
            cameraTransform.rotation = Quaternion.Euler(eulerAngles);
            _offsetCache.Store(cameraTransform, Quaternion.identity, eulerAngles, false);
        }

        /// <summary>
        /// ビューポート (画面内での表示位置と大きさ) の 4 行。
        /// カメラへの反映は UpdateCameraViewport 経由で行う (レイヤー UI と同じ)
        /// </summary>
        private void DrawViewportRows(
            GUIView view, MTEP.SubCameraData cameraData, float labelWidth, float rowHeight,
            Action<string> recordEdit)
        {
            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("ビューポート設定", -1, rowHeight);

            var viewportRect = cameraData.viewportRect;
            var updated = false;

            var defaultViewport = MTEP.SubCameraManager.DefaultViewport;
            updated |= DrawViewportSlider(view, "X", viewportRect.x, defaultViewport.x,
                labelWidth, value => viewportRect.x = value);
            updated |= DrawViewportSlider(view, "Y", viewportRect.y, defaultViewport.y,
                labelWidth, value => viewportRect.y = value);
            updated |= DrawViewportSlider(view, "幅", viewportRect.width, defaultViewport.width,
                labelWidth, value => viewportRect.width = value);
            updated |= DrawViewportSlider(view, "高さ", viewportRect.height,
                defaultViewport.height, labelWidth, value => viewportRect.height = value);

            if (updated)
            {
                // viewportRect はローカルコピーなので、反映前に呼べば変更前を捕捉できる
                recordEdit("ビューポート");
                subCameraManager.UpdateCameraViewport(cameraData.name, viewportRect);
            }
        }

        private static bool DrawViewportSlider(
            GUIView view, string label, float value, float defaultValue,
            float labelWidth, Action<float> onChanged)
        {
            return view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = 0,
                max = 1,
                step = 0.01f,
                defaultValue = defaultValue,
                value = value,
                onChanged = onChanged,
            });
        }
    }
}
