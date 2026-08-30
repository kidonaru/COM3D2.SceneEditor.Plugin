using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("ステージライト", 42)]
    public class StageLightTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(StageLightTimelineLayer);
        public override string layerName => nameof(StageLightTimelineLayer);

        private List<string> _allBoneNames = null;

        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>(stageLightManager.lightNames.Count + stageLightManager.controllerNames.Count);
                    _allBoneNames.AddRange(stageLightManager.lightNames);
                    _allBoneNames.AddRange(stageLightManager.controllerNames);
                }
                return _allBoneNames;
            }
        }

        private StageLightTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static StageLightTimelineLayer Create(int slotNo)
        {
            return new StageLightTimelineLayer(0);
        }

        public static bool ValidateLayer()
        {
            return bundleManager.IsValid();
        }

        public override void Init()
        {
            base.Init();

            StageLightManager.onControllerAdded += OnControllerAdded;
            StageLightManager.onControllerRemoved += OnControllerRemoved;
            StageLightManager.onLightAdded += OnLightAdded;
            StageLightManager.onLightRemoved += OnLightRemoved;

            InitMenuItems();
        }

        protected override void InitMenuItems()
        {
            _allBoneNames = null;
            allMenuItems.Clear();

            foreach (var controller in stageLightManager.controllers)
            {
                var name = "StageLightGroup (" + controller.groupIndex + ")";
                var displayName = "グループ (" + controller.groupIndex + ")";
                var setMenuItem = new BoneSetMenuItem(name, displayName);
                allMenuItems.Add(setMenuItem);

                {
                    var menuItem = new BoneMenuItem(controller.name, controller.displayName);
                    setMenuItem.AddChild(menuItem);
                }

                foreach (var light in controller.lights)
                {
                    var menuItem = new BoneMenuItem(light.name, light.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            StageLightManager.onControllerAdded -= OnControllerAdded;
            StageLightManager.onControllerRemoved -= OnControllerRemoved;
            StageLightManager.onLightAdded -= OnLightAdded;
            StageLightManager.onLightRemoved -= OnLightRemoved;
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

            ApplyPlayDataByType(TransformType.StageLightController);
            ApplyPlayDataByType(TransformType.StageLight);

            foreach (var controller in stageLightManager.controllers)
            {
                controller.UpdateLights();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.StageLight:
                    if (indexUpdated)
                    {
                        ApplyLightMotionInit(motion, t);
                    }
                    ApplyLightMotionUpdate(motion, t);
                    break;
                case TransformType.StageLightController:
                    if (indexUpdated)
                    {
                        ApplyControllerMotionInit(motion, t);
                    }
                    ApplyControllerMotionUpdate(motion, t);
                    break;
            }
        }

        private void ApplyLightMotionInit(MotionData motion, float t)
        {
            var light = stageLightManager.GetLight(motion.name);
            if (light == null)
            {
                return;
            }

            var controller = light.controller;
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLight;

            if (!controller.autoVisible)
            {
                light.visible = start.visible;
            }

            if (!controller.autoPosition)
            {
                light.position = start.position;
            }

            if (!controller.autoRotation)
            {
                light.eulerAngles = start.eulerAngles;
            }

            if (!controller.autoColor)
            {
                light.color = start.color;
            }

            if (!controller.autoLightInfo)
            {
                light.spotAngle = start.spotAngle;
                light.spotRange = start.spotRange;

                light.rangeMultiplier = start.rangeMultiplier;
                light.falloffExp = start.falloffExp;
                light.noiseStrength = start.noiseStrength;
                light.noiseScale = start.noiseScale;
                light.coreRadius = start.coreRadius;
                light.offsetRange = start.offsetRange;
                light.segmentAngle = start.segmentAngle;
                light.segmentRange = start.segmentRange;
                light.zTest = start.zTest;
            }
        }

        private void ApplyLightMotionUpdate(MotionData motion, float t)
        {
            var light = stageLightManager.GetLight(motion.name);
            if (light == null)
            {
                return;
            }

            var controller = light.controller;
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLight;
            var end = motion.end as TransformDataStageLight;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (!controller.autoVisible)
            {
                light.visible = start.visible;
            }

            if (!controller.autoPosition)
            {
                light.position = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.positionValues,
                    end.positionValues,
                    t
                ).ToVector3();
            }

            if (!controller.autoRotation)
            {
                light.eulerAngles = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.eulerAnglesValues,
                    end.eulerAnglesValues,
                    t
                ).ToVector3();
            }

            if (!controller.autoColor)
            {
                light.color = Color.Lerp(start.color, end.color, t);
            }

            if (!controller.autoLightInfo)
            {
                light.spotAngle = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.spotAngleValue,
                    end.spotAngleValue,
                    t);

                light.spotRange = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.spotRangeValue,
                    end.spotRangeValue,
                    t);
            }
        }

        private void ApplyControllerMotionInit(MotionData motion, float t)
        {
            var controller = stageLightManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLightController;

            controller.visible = start.visible;

            controller.positionMin = start.position;
            controller.positionMax = start.subPosition;
            controller.rotationMin = start.eulerAngles;
            controller.rotationMax = start.subEulerAngles;
            controller.colorMin = start.color;
            controller.colorMax = start.subColor;

            var lightInfo = controller.lightInfo;

            lightInfo.spotAngle = start.spotAngle;
            lightInfo.spotRange = start.spotRange;

            lightInfo.rangeMultiplier = start.rangeMultiplier;
            lightInfo.falloffExp = start.falloffExp;
            lightInfo.noiseStrength = start.noiseStrength;
            lightInfo.noiseScale = start.noiseScale;
            lightInfo.coreRadius = start.coreRadius;
            lightInfo.offsetRange = start.offsetRange;
            lightInfo.segmentAngle = start.segmentAngle;
            lightInfo.segmentRange = start.segmentRange;
            lightInfo.zTest = start.zTest;

            controller.autoPosition = start.autoPosition;
            controller.autoRotation = start.autoRotation;
            controller.autoColor = start.autoColor;
            controller.autoLightInfo = start.autoLightInfo;
            controller.autoVisible = start.autoVisible;
        }

        private void ApplyControllerMotionUpdate(MotionData motion, float t)
        {
            var controller = stageLightManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataStageLightController;
            var end = motion.end as TransformDataStageLightController;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (controller.autoPosition)
            {
                controller.positionMin = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.positionValues,
                    end.positionValues,
                    t
                ).ToVector3();

                controller.positionMax = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.subPositionValues,
                    end.subPositionValues,
                    t
                ).ToVector3();
            }

            if (controller.autoRotation)
            {
                controller.rotationMin = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.eulerAnglesValues,
                    end.eulerAnglesValues,
                    t
                ).ToVector3();

                controller.rotationMax = PluginUtils.HermiteValues(
                    t0,
                    t1,
                    start.subEulerAnglesValues,
                    end.subEulerAnglesValues,
                    t
                ).ToVector3();
            }

            if (controller.autoColor)
            {
                controller.colorMin = Color.Lerp(start.color, end.color, t);
                controller.colorMax = Color.Lerp(start.subColor, end.subColor, t);
            }

            var lightInfo = controller.lightInfo;

            if (controller.autoLightInfo)
            {
                lightInfo.spotAngle = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.spotAngleValue,
                    end.spotAngleValue,
                    t);

                lightInfo.spotRange = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.spotRangeValue,
                    end.spotRangeValue,
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

        public void OnLightAdded(string lightName)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { lightName });
            ApplyCurrentFrame(true);
        }

        public void OnLightRemoved(string lightName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { lightName });
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var light in stageLightManager.lights)
            {
                if (light == null || light.transform == null)
                {
                    continue;
                }

                var lightName = light.name;

                var trans = CreateTransformData<TransformDataStageLight>(lightName);
                trans.FromStageLight(light);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            foreach (var controller in stageLightManager.controllers)
            {
                if (controller == null || controller.transform == null)
                {
                    continue;
                }

                var controllerName = controller.name;

                var trans = CreateTransformData<TransformDataStageLightController>(controllerName);
                trans.FromStageLightController(controller);

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
            if (name.StartsWith("StageLightController", StringComparison.Ordinal))
            {
                return TransformType.StageLightController;
            }
            else
            {
                return TransformType.StageLight;
            }
        }
    }
}