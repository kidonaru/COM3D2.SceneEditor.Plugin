using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("カメラ", 20, TimelineLayerCategory.Camera)]
    public class CameraTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(CameraTimelineLayer);
        public override string layerName => nameof(CameraTimelineLayer);

        public override bool isCameraLayer => true;

        private static Camera camera => PluginUtils.MainCamera;
        private static Camera subCamera => studioHack.subCamera;
        private static MaidFollowMainCamera mainFollow => MaidFollowMainCamera.instance;

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

            var start = motion.start as TransformDataCamera;
            var end = motion.end as TransformDataCamera;

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
            uoCamera.SetDistance(distance);
            uoCamera.SetAroundAngle(new Vector2(eulerAngles.y, eulerAngles.x));
            camera.SetRotationZ(eulerAngles.z);
            camera.fieldOfView = viewAngle;

            // 追従設定は補間せず区間の始点の値を使う (サブカメラレイヤーと同じ)。
            // 追従中は position がオフセット、向き反映中は yaw がヨーオフセットになり、
            // 実際の注視点は MaidFollowMainCamera が LateUpdate で書く
            var follow = mainFollow;
            if (follow == null)
            {
                uoCamera.SetTargetPos(position);
            }
            else
            {
                follow.state.maidSlotNo = start.maidSlotNo;
                follow.state.maidPointType = start.maidPointType;
                follow.state.followRotation = start.followRotation;

                if (follow.isFollow)
                {
                    follow.state.offset = position;
                    follow.state.yawOffset = eulerAngles.y;
                    follow.Apply();
                }
                else
                {
                    uoCamera.SetTargetPos(position);
                }
            }

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

            // 追従中は注視点の代わりにオフセットを保存する (ApplyMotion と対称)
            var follow = mainFollow;
            if (follow != null)
            {
                trans.maidSlotNo = follow.state.maidSlotNo;
                trans.maidPointType = follow.state.maidPointType;
                trans.followRotation = follow.state.followRotation;

                if (follow.isFollow)
                {
                    trans.position = follow.state.offset;
                    if (follow.state.followRotation)
                    {
                        trans.eulerAngles = new Vector3(angle.y, follow.state.yawOffset, rotZ);
                    }
                }
            }

            var bone = frame.CreateBone(trans);
            frame.UpdateBone(bone);

            //MTEUtils.LogDebug("UpdateFromCurrentPose: position={0}, rotation={1}", _cameraManager.CurrentPosition,_cameraManager.CurrentRotation);
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Camera;
        }
    }
}