using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // MTE 原本の priority 21 は ModelTimelineLayer と重複するため SE では 25 に変更
    [TimelineLayerDesc("サブカメラ", 25)]
    public class SubCameraTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(SubCameraTimelineLayer);
        public override string layerName => nameof(SubCameraTimelineLayer);

        public override bool isCameraLayer => true;

        public override List<string> allBoneNames => subCameraManager.subCameraNames;

        private static SubCameraManager subCameraManager => SubCameraManager.instance;

        private SubCameraData currentCamera;

        private SubCameraTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static SubCameraTimelineLayer Create(int slotNo)
        {
            return new SubCameraTimelineLayer(0);
        }

        public override void Init()
        {
            // base.Init()内のInitMenuItemsでカメラを参照するため先に生成する
            subCameraManager.SetupCameras();

            base.Init();

            AddFirstBones(allBoneNames);

            SubCameraManager.onCameraAdded += OnCameraAdded;
            SubCameraManager.onCameraRemoved += OnCameraRemoved;
        }

        public override void Dispose()
        {
            base.Dispose();

            SubCameraManager.onCameraAdded -= OnCameraAdded;
            SubCameraManager.onCameraRemoved -= OnCameraRemoved;

            // レイヤー削除後もカメラが残らないよう破棄する
            subCameraManager.DestroyAllCameras();
        }

        public void OnCameraAdded(SubCameraData cameraData)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { cameraData.name });
            ApplyCurrentFrame(true);
        }

        public void OnCameraRemoved(SubCameraData cameraData)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { cameraData.name });
            if (currentCamera == cameraData)
            {
                currentCamera = null;
            }
            ApplyCurrentFrame(true);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var cameraData in subCameraManager.subCameras)
            {
                var menuItem = new BoneMenuItem(cameraData.name, cameraData.displayName);
                allMenuItems.Add(menuItem);
            }
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
            var cameraData = subCameraManager.GetOrCreateCamera(motion.name);
            if (cameraData == null || cameraData.camera == null)
            {
                return;
            }

            var start = motion.start as TransformDataSubCamera;
            var end = motion.end as TransformDataSubCamera;

            if (indexUpdated)
            {
                cameraData.visible = start.visible;
                cameraData.follow.maidSlotNo = start.maidSlotNo;
                cameraData.follow.maidPointType = start.maidPointType;
                cameraData.follow.followRotation = start.followRotation;
            }

            Vector3 position, eulerAngles;
            float fov;

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

            fov = PluginUtils.HermiteValue(
                t0,
                t1,
                start.fovValue,
                end.fovValue,
                t);

            var startViewport = start.viewport;
            var endViewport = end.viewport;
            var viewportRect = new Rect(
                Mathf.Lerp(startViewport.x, endViewport.x, t),
                Mathf.Lerp(startViewport.y, endViewport.y, t),
                Mathf.Lerp(startViewport.width, endViewport.width, t),
                Mathf.Lerp(startViewport.height, endViewport.height, t)
            );

            cameraData.position = position;
            cameraData.rotation = Quaternion.Euler(eulerAngles);
            cameraData.camera.fieldOfView = fov;
            cameraData.ApplyViewport(viewportRect);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var cameraData in subCameraManager.subCameras)
            {
                if (cameraData == null || cameraData.camera == null)
                {
                    continue;
                }

                var cameraName = cameraData.name;

                var trans = CreateTransformData<TransformDataSubCamera>(cameraName);
                trans.position = cameraData.position;
                trans.eulerAngles = cameraData.rotation.eulerAngles;
                trans.fov = cameraData.camera.fieldOfView;
                trans.viewport = cameraData.viewportRect;
                trans.maidSlotNo = cameraData.follow.maidSlotNo;
                trans.maidPointType = cameraData.follow.maidPointType;
                trans.followRotation = cameraData.follow.followRotation;
                trans.visible = cameraData.visible;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.SubCamera;
        }
    }
}
