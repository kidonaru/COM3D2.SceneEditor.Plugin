using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("カメラ", 20)]
    public class CameraTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(CameraTimelineLayer);
        public override string layerName => nameof(CameraTimelineLayer);

        public override bool isCameraLayer => true;

        private static Camera camera => PluginUtils.MainCamera;
        private static Camera subCamera => studioHack.subCamera;

        public static string CameraBoneName = "camera";
        public static string CameraDisplayName = "カメラ";

        private List<string> _allBoneNames = new List<string> { CameraBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private CameraTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static CameraTimelineLayer Create(int slotNo)
        {
            return new CameraTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(CameraBoneName, CameraDisplayName);
            allMenuItems.Add(menuItem);
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
        }

        protected override void ApplyPlayData()
        {
            if (!isCurrent && !config.isCameraSync)
            {
                return;
            }

            base.ApplyPlayData();
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            Vector3 position, eulerAngles;
            float distance, viewAngle;

            var start = motion.start;
            var end = motion.end;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            position = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            eulerAngles = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.eulerAnglesValues,
                end.eulerAnglesValues,
                t);

            var tempScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);

            distance = tempScale.x;
            viewAngle = tempScale.y;


            if (config.isFixedFoV && !isCurrent && studioHackManager.isPoseEditing)
            {
                viewAngle = 35;
            }

            var uoCamera = GetUOCamera();
            uoCamera.SetTargetPos(position);
            uoCamera.SetDistance(distance);
            uoCamera.SetAroundAngle(new Vector2(eulerAngles.y, eulerAngles.x));
            camera.SetRotationZ(eulerAngles.z);
            camera.fieldOfView = viewAngle;

            if (subCamera != null)
            {
                subCamera.fieldOfView = viewAngle;
            }

            if (config.isFixedFocus && !isCurrent && studioHackManager.isPoseEditing)
            {
                var currentMaidCache = maidManager.maidCache;
                if (currentMaidCache != null)
                {
                    var target = currentMaidCache.GetPointTransform(MaidPointType.Head);
                    uoCamera.MoveTarget(target.position);
                }
            }

            //MTEUtils.LogDebug("ApplyMotion: position={0}, rotation={1}, distance={2}, viewAngle={3}", position, rotation, distance, viewAngle);
        }

        public static UltimateOrbitCamera GetUOCamera()
        {
            return camera.GetComponent<UltimateOrbitCamera>();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var uoCamera = GetUOCamera();
            var target = uoCamera.target;
            var angle = uoCamera.GetAroundAngle();
            var rotZ = camera.GetRotationZ();

            var trans = CreateTransformData<TransformDataCamera>(CameraBoneName);
            trans.position = target.position;
            trans.eulerAngles = new Vector3(angle.y, angle.x, rotZ);
            trans.scale = new Vector3(uoCamera.distance, camera.fieldOfView, 0);

            var bone = frame.CreateBone(trans);
            frame.UpdateBone(bone);

            //MTEUtils.LogDebug("UpdateFromCurrentPose: position={0}, rotation={1}", _cameraManager.CurrentPosition,_cameraManager.CurrentRotation);
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            // カメラの編集 UI は SE の CameraWindow に委譲する (レイヤー UI 非接続方針)
            // 回転の 360 度跨ぎは GetAnmBinary の FixRotation が前キー基準で補正するため、UI 側の補正は不要
            view.DrawLabel("カメラの編集は カメラウィンドウで行ってください", -1, 20);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Camera;
        }
    }
}