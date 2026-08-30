using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("ステージレーザー", 43)]
    public class StageLaserTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(StageLaserTimelineLayer);
        public override string layerName => nameof(StageLaserTimelineLayer);

        private List<string> _allBoneNames = null;

        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>(stageLaserManager.laserNames.Count + stageLaserManager.controllerNames.Count);
                    _allBoneNames.AddRange(stageLaserManager.laserNames);
                    _allBoneNames.AddRange(stageLaserManager.controllerNames);
                }
                return _allBoneNames;
            }
        }

        private StageLaserTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static StageLaserTimelineLayer Create(int slotNo)
        {
            return new StageLaserTimelineLayer(0);
        }

        public static bool ValidateLayer()
        {
            return bundleManager.IsValid();
        }

        public override void Init()
        {
            base.Init();

            StageLaserManager.onControllerAdded += OnControllerAdded;
            StageLaserManager.onControllerRemoved += OnControllerRemoved;
            StageLaserManager.onLaserAdded += OnLaserAdded;
            StageLaserManager.onLaserRemoved += OnLaserRemoved;

            InitMenuItems();
        }

        protected override void InitMenuItems()
        {
            _allBoneNames = null;
            allMenuItems.Clear();

            foreach (var controller in stageLaserManager.controllers)
            {
                var name = "StageLaserGroup (" + controller.groupIndex + ")";
                var displayName = "グループ (" + controller.groupIndex + ")";
                var setMenuItem = new BoneSetMenuItem(name, displayName);
                allMenuItems.Add(setMenuItem);

                {
                    var menuItem = new BoneMenuItem(controller.name, controller.displayName);
                    setMenuItem.AddChild(menuItem);
                }

                foreach (var laser in controller.lasers)
                {
                    var menuItem = new BoneMenuItem(laser.name, laser.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            StageLaserManager.onControllerAdded -= OnControllerAdded;
            StageLaserManager.onControllerRemoved -= OnControllerRemoved;
            StageLaserManager.onLaserAdded -= OnLaserAdded;
            StageLaserManager.onLaserRemoved -= OnLaserRemoved;
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
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            ApplyPlayDataByType(TransformType.StageLaserController);
            ApplyPlayDataByType(TransformType.StageLaser);

            foreach (var controller in stageLaserManager.controllers)
            {
                controller.UpdateLasers();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.StageLaser:
                    if (indexUpdated)
                    {
                        ApplyLaserMotionInit(motion, t);
                    }
                    ApplyLaserMotionUpdate(motion, t);
                    break;
                case TransformType.StageLaserController:
                    if (indexUpdated)
                    {
                        ApplyControllerMotionInit(motion, t);
                    }
                    ApplyControllerMotionUpdate(motion, t);
                    break;
            }
        }

        private void ApplyLaserMotionInit(MotionData motion, float t)
        {
            var laser = stageLaserManager.GetLaser(motion.name);
            if (laser == null)
            {
                return;
            }

            var controller = laser.controller;
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLaser;

            if (!controller.autoVisible)
            {
                laser.visible = start.visible;
            }

            if (!controller.autoRotation)
            {
                laser.eulerAngles = start.eulerAngles;
            }

            if (!controller.autoColor)
            {
                laser.color1 = start.color;
                laser.color2 = start.subColor;
            }

            if (!controller.autoLaserInfo)
            {
                laser.intensity = start.intensity;
                laser.laserRange = start.laserRange;
                laser.laserWidth = start.laserWidth;

                laser.falloffExp = start.falloffExp;
                laser.noiseStrength = start.noiseStrength;
                laser.noiseScale = start.noiseScale;
                laser.coreRadius = start.coreRadius;
                laser.offsetRange = start.offsetRange;
                laser.glowWidth = start.glowWidth;
                laser.segmentRange = start.segmentRange;
                laser.zTest = start.zTest;
            }
        }

        private void ApplyLaserMotionUpdate(MotionData motion, float t)
        {
            var laser = stageLaserManager.GetLaser(motion.name);
            if (laser == null)
            {
                return;
            }

            var controller = laser.controller;
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLaser;
            var end = motion.end as TransformDataStageLaser;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (!controller.autoRotation)
            {
                laser.eulerAngles = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.eulerAnglesValues,
                    end.eulerAnglesValues,
                    t
                ).ToVector3();
            }

            if (!controller.autoColor)
            {
                laser.color1 = Color.Lerp(start.color, end.color, t);
                laser.color2 = Color.Lerp(start.subColor, end.subColor, t);
            }

            if (!controller.autoLaserInfo)
            {
                laser.intensity = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityValue,
                    end.intensityValue,
                    t);

                laser.laserRange = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.laserRangeValue,
                    end.laserRangeValue,
                    t);

                laser.laserWidth = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.laserWidthValue,
                    end.laserWidthValue,
                    t);
            }
        }

        private void ApplyControllerMotionInit(MotionData motion, float t)
        {
            var controller = stageLaserManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLaserController;

            controller.visible = start.visible;

            controller.position = start.position;
            controller.eulerAngles = start.eulerAngles;
            controller.rotationMin = start.rotationMin;
            controller.rotationMax = start.rotationMax;
            controller.color1 = start.color;
            controller.color2 = start.subColor;

            var laserInfo = controller.laserInfo;

            laserInfo.intensity = start.intensity;
            laserInfo.laserRange = start.laserRange;
            laserInfo.laserWidth = start.laserWidth;

            laserInfo.falloffExp = start.falloffExp;
            laserInfo.noiseStrength = start.noiseStrength;
            laserInfo.noiseScale = start.noiseScale;
            laserInfo.coreRadius = start.coreRadius;
            laserInfo.offsetRange = start.offsetRange;
            laserInfo.glowWidth = start.glowWidth;
            laserInfo.segmentRange = start.segmentRange;
            laserInfo.zTest = start.zTest;

            controller.autoRotation = start.autoRotation;
            controller.autoColor = start.autoColor;
            controller.autoLaserInfo = start.autoLaserInfo;
            controller.autoVisible = start.autoVisible;
        }

        private void ApplyControllerMotionUpdate(MotionData motion, float t)
        {
            var controller = stageLaserManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLaserController;
            var end = motion.end as TransformDataStageLaserController;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            controller.position = PluginUtils.HermiteValues(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t
            ).ToVector3();

            controller.eulerAngles = PluginUtils.HermiteValues(
                t0,
                t1,
                start.eulerAnglesValues,
                end.eulerAnglesValues,
                t
            ).ToVector3();

            if (controller.autoRotation)
            {
                controller.rotationMin = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.rotationMinValues,
                    end.rotationMinValues,
                    t
                ).ToVector3();

                controller.rotationMax = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.rotationMaxValues,
                    end.rotationMaxValues,
                    t
                ).ToVector3();
            }

            if (controller.autoColor)
            {
                controller.color1 = Color.Lerp(start.color, end.color, t);
                controller.color2 = Color.Lerp(start.subColor, end.subColor, t);
            }

            var laserInfo = controller.laserInfo;

            if (controller.autoLaserInfo)
            {
                laserInfo.intensity = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityValue,
                    end.intensityValue,
                    t);

                laserInfo.laserRange = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.laserRangeValue,
                    end.laserRangeValue,
                    t);

                laserInfo.laserWidth = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.laserWidthValue,
                    end.laserWidthValue,
                    t);
            }
        }

        public void OnControllerAdded(string controllerName)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { controllerName });
            ApplyCurrentFrame(true);
        }

        public void OnControllerRemoved(string controllerName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { controllerName });
            ApplyCurrentFrame(true);
        }

        public void OnLaserAdded(string laserName)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { laserName });
            ApplyCurrentFrame(true);
        }

        public void OnLaserRemoved(string laserName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { laserName });
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var laser in stageLaserManager.lasers)
            {
                if (laser == null || laser.transform == null)
                {
                    continue;
                }

                var laserName = laser.name;

                var trans = CreateTransformData<TransformDataStageLaser>(laserName);
                trans.FromStageLaser(laser);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            foreach (var controller in stageLaserManager.controllers)
            {
                if (controller == null || controller.transform == null)
                {
                    continue;
                }

                var controllerName = controller.name;

                var trans = CreateTransformData<TransformDataStageLaserController>(controllerName);
                trans.FromStageLaserController(controller);

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
            if (name.StartsWith("StageLaserController", StringComparison.Ordinal))
            {
                return TransformType.StageLaserController;
            }
            else
            {
                return TransformType.StageLaser;
            }
        }
    }
}