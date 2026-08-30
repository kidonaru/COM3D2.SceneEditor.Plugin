using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メインカメラの構図 (注視点・ヨー/ピッチ/ロール・距離・FOV) の行描画。
    /// CameraWindow と TimelineItemInspector (カメラレイヤーの項目表示) で共有する。
    /// 編集は UltimateOrbitCamera を包む CameraMain の API 経由で行う。
    /// 履歴 (HistoryScope.Camera) を記録するのは RecordCameraEdit を呼ぶ行だけで、
    /// 汎用の DrawAxisSlider は記録しない (履歴対象外の SceneView カメラでも使うため)
    /// </summary>
    public static class MainCameraRowDrawer
    {
        // リセット時の既定値 (CameraMain.Reset の Target カメラ初期値に合わせる)
        public static readonly Vector3 DefaultTargetPos = new Vector3(0f, 1.5f, 0f);
        public static readonly float DefaultDistance = 2f;
        public static readonly Vector2 DefaultAroundAngle = new Vector2(180f, 10f);
        public static readonly float DefaultFov = 35f;

        /// <summary>座標行 (Inspector の座標行と同じ形式) のドラッグ感度</summary>
        public const float PositionDragSensitivity = 0.01f;

        /// <summary>注視点のワールド座標。Inspector の座標行と同じ表示形式で編集する</summary>
        public static void DrawTargetPosRow(
            GUIView view, CameraMain mainCamera, float labelWidth, float rowHeight)
        {
            Vector3RowDrawer.Draw(view, "注視点", PositionDragSensitivity, labelWidth, rowHeight,
                mainCamera.GetTargetPos(),
                value =>
                {
                    RecordCameraEdit("注視点");
                    mainCamera.SetTargetPos(value);
                },
                () =>
                {
                    RecordCameraEdit("注視点");
                    mainCamera.SetTargetPos(DefaultTargetPos);
                });
        }

        /// <summary>
        /// 回転。GetAroundAngle は x がヨー (水平旋回)、y がピッチ (仰俯角)。
        /// ロールは UltimateOrbitCamera が管理しないため Transform へ直接書く
        /// </summary>
        public static void DrawAngleSliders(
            GUIView view, CameraMain mainCamera, Camera camera,
            float labelWidth, float rowHeight)
        {
            var aroundAngle = mainCamera.GetAroundAngle();

            // 旋回中は値が際限なく積み上がるため、表示は ±180 度へ正規化する
            var yaw = NormalizeAngle(aroundAngle.x);
            var pitch = NormalizeAngle(aroundAngle.y);
            var roll = NormalizeAngle(camera.transform.eulerAngles.z);

            DrawAxisSlider(view, "ヨー", yaw, -180f, 180f, 0.1f,
                NormalizeAngle(DefaultAroundAngle.x), labelWidth, rowHeight, value =>
                {
                    RecordCameraEdit("ヨー");
                    mainCamera.SetAroundAngle(new Vector2(value, pitch));
                });
            DrawAxisSlider(view, "ピッチ", pitch, -90f, 90f, 0.1f,
                DefaultAroundAngle.y, labelWidth, rowHeight, value =>
                {
                    RecordCameraEdit("ピッチ");
                    mainCamera.SetAroundAngle(new Vector2(yaw, value));
                });
            DrawAxisSlider(view, "ロール", roll, -180f, 180f, 0.1f, 0f,
                labelWidth, rowHeight, value =>
                {
                    RecordCameraEdit("ロール");
                    var eulerAngles = camera.transform.eulerAngles;
                    eulerAngles.z = value;
                    camera.transform.eulerAngles = eulerAngles;
                });
        }

        /// <summary>注視点からの距離と視野角</summary>
        public static void DrawDistanceFovSliders(
            GUIView view, CameraMain mainCamera, Camera camera,
            float labelWidth, float rowHeight)
        {
            DrawAxisSlider(view, "距離", mainCamera.GetDistance(), 0.1f, 30f, 0.01f,
                DefaultDistance, labelWidth, rowHeight, value =>
                {
                    RecordCameraEdit("距離");
                    mainCamera.SetDistance(value);
                });

            DrawAxisSlider(view, "FOV", camera.fieldOfView, 1f, 179f, 0.1f,
                DefaultFov, labelWidth, rowHeight, value =>
                {
                    RecordCameraEdit("FOV");
                    camera.fieldOfView = value;
                });
        }

        /// <summary>メインカメラの操作を履歴へ記録する。SceneView カメラは対象にしない</summary>
        public static void RecordCameraEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Camera, "カメラ: " + label);
        }

        /// <summary>共通書式のスライダー 1 行</summary>
        public static void DrawAxisSlider(
            GUIView view, string label, float value, float min, float max, float step,
            float defaultValue, float labelWidth, float rowHeight, Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
                width = -1,
                min = min,
                max = max,
                step = step,
                defaultValue = defaultValue,
                value = value,
                onChanged = onChanged,
            });
        }

        /// <summary>角度を (-180, 180] へ正規化する</summary>
        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
