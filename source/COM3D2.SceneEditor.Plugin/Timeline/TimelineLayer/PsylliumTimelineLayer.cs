using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("サイリウム", 44)]
    public class PsylliumTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(PsylliumTimelineLayer);
        public override string layerName => nameof(PsylliumTimelineLayer);

        private List<string> _allBoneNames = null;

        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>(
                        psylliumManager.controllerNames.Count +
                        psylliumManager.barConfigNames.Count +
                        psylliumManager.handConfigNames.Count +
                        psylliumManager.areaNames.Count +
                        psylliumManager.patternConfigNames.Count +
                        psylliumManager.transformConfigNames.Count);

                    _allBoneNames.AddRange(psylliumManager.controllerNames);
                    _allBoneNames.AddRange(psylliumManager.barConfigNames);
                    _allBoneNames.AddRange(psylliumManager.handConfigNames);
                    _allBoneNames.AddRange(psylliumManager.areaNames);
                    _allBoneNames.AddRange(psylliumManager.patternConfigNames);
                    _allBoneNames.AddRange(psylliumManager.transformConfigNames);
                }
                return _allBoneNames;
            }
        }

        private PsylliumTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static PsylliumTimelineLayer Create(int slotNo)
        {
            return new PsylliumTimelineLayer(0);
        }

        public static bool ValidateLayer()
        {
            return bundleManager.IsValid();
        }

        public override void Init()
        {
            base.Init();

            PsylliumManager.onControllerAdded += OnControllerAdded;
            PsylliumManager.onControllerRemoved += OnControllerRemoved;
            PsylliumManager.onAreaAdded += OnAreaAdded;
            PsylliumManager.onAreaRemoved += OnAreaRemoved;
            PsylliumManager.onPatternAdded += OnPatternAdded;
            PsylliumManager.onPatternRemoved += OnPatternRemoved;
        }

        protected override void InitMenuItems()
        {
            _allBoneNames = null;
            allMenuItems.Clear();

            foreach (var controller in psylliumManager.controllers)
            {
                var name = "PsylliumGroup (" + controller.groupIndex + ")";
                var displayName = "グループ (" + controller.groupIndex + ")";
                var setMenuItem = new BoneSetMenuItem(name, displayName);
                allMenuItems.Add(setMenuItem);

                {
                    var menuItem = new BoneMenuItem(controller.name, controller.displayName);
                    setMenuItem.AddChild(menuItem);
                }

                {
                    var barConfig = controller.barConfig;
                    var menuItem = new BoneMenuItem(barConfig.name, barConfig.displayName);
                    setMenuItem.AddChild(menuItem);
                }

                {
                    var handConfig = controller.handConfig;
                    var menuItem = new BoneMenuItem(handConfig.name, handConfig.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }

            var areaSetMenuItem = new BoneSetMenuItem("PsylliumArea", "エリア");
            allMenuItems.Add(areaSetMenuItem);

            foreach (var area in psylliumManager.areas)
            {
                var menuItem = new BoneMenuItem(area.name, area.displayName);
                areaSetMenuItem.AddChild(menuItem);
            }

            var patternSetMenuItem = new BoneSetMenuItem("PsylliumPattern", "パターン");
            allMenuItems.Add(patternSetMenuItem);

            var transformSetMenuItem = new BoneSetMenuItem("PsylliumTransform", "移動回転");
            allMenuItems.Add(transformSetMenuItem);

            foreach (var pattern in psylliumManager.patterns)
            {
                var menuItem = new BoneMenuItem(pattern.patternConfig.name, pattern.patternConfig.displayName);
                patternSetMenuItem.AddChild(menuItem);

                var menuItem2 = new BoneMenuItem(pattern.transformConfig.name, pattern.transformConfig.displayName);
                transformSetMenuItem.AddChild(menuItem2);
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            PsylliumManager.onControllerAdded -= OnControllerAdded;
            PsylliumManager.onControllerRemoved -= OnControllerRemoved;
            PsylliumManager.onAreaAdded -= OnAreaAdded;
            PsylliumManager.onAreaRemoved -= OnAreaRemoved;
            PsylliumManager.onPatternAdded -= OnPatternAdded;
            PsylliumManager.onPatternRemoved -= OnPatternRemoved;
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

            ApplyPlayDataByType(TransformType.PsylliumController);
            ApplyPlayDataByType(TransformType.PsylliumArea);
            ApplyPlayDataByType(TransformType.PsylliumBar);
            ApplyPlayDataByType(TransformType.PsylliumHand);
            ApplyPlayDataByType(TransformType.PsylliumPattern);
            ApplyPlayDataByType(TransformType.PsylliumTransform);

            ApplyTempPlayData();

            var playingTime = this.playingTime;
            foreach (var controller in psylliumManager.controllers)
            {
                controller.ManualUpdate(playingTime);
            }
        }

        protected Dictionary<string, MotionPlayData> _tempPlayDataMap = new Dictionary<string, MotionPlayData>(32);

        protected override void BuildPlayData()
        {
            base.BuildPlayData();

            foreach (var playData in _tempPlayDataMap.Values)
            {
                playData.ResetIndex();
                playData.motions = null;
            }

            foreach (var pair in _playDataMap)
            {
                var name = pair.Key;
                var playData = pair.Value;

                if (playData.motions.Count == 0)
                {
                    continue;
                }

                MotionPlayData tempPlayData;
                if (!_tempPlayDataMap.TryGetValue(name, out tempPlayData))
                {
                    tempPlayData = new MotionPlayData(playData.motions.Count);
                    _tempPlayDataMap[name] = tempPlayData;
                }

                tempPlayData.motions = playData.motions;
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.PsylliumController:
                    if (indexUpdated)
                    {
                        ApplyControllerMotionInit(motion, t);
                    }
                    break;
                case TransformType.PsylliumBar:
                    if (indexUpdated)
                    {
                        ApplyBarConfigMotionInit(motion, t);
                    }
                    break;
                case TransformType.PsylliumHand:
                    if (indexUpdated)
                    {
                        ApplyHandConfigMotionInit(motion, t);
                    }
                    break;
                case TransformType.PsylliumArea:
                    if (indexUpdated)
                    {
                        ApplyAreaMotionInit(motion, t);
                    }
                    break;
                case TransformType.PsylliumPattern:
                    if (indexUpdated)
                    {
                        ApplyPatternMotionInit(motion, t);
                    }
                    ApplyPatternMotionUpdate(motion, t);
                    break;
                case TransformType.PsylliumTransform:
                    if (indexUpdated)
                    {
                        ApplyTransformMotionInit(motion, t);
                    }
                    ApplyTransformMotionUpdate(motion, t);
                    break;
            }
        }

        private void ApplyControllerMotionInit(MotionData motion, float t)
        {
            var controller = psylliumManager.GetController(motion.name);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumController;

            controller.visible = start.visible;
            controller.position = start.position;
            controller.eulerAngles = start.eulerAngles;

            controller.refreshRequired = true;
        }

        private void ApplyBarConfigMotionInit(MotionData motion, float t)
        {
            var barConfig = psylliumManager.GetBarConfig(motion.name);
            if (barConfig == null)
            {
                return;
            }

            var controller = psylliumManager.GetController(barConfig.groupIndex);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumBar;
            var targetConfig = start.ToConfig();

            if (targetConfig.Equals(barConfig))
            {
                return;
            }

            barConfig.CopyFrom(targetConfig);
            controller.refreshRequired = true;
        }

        private void ApplyHandConfigMotionInit(MotionData motion, float t)
        {
            var handConfig = psylliumManager.GetHandConfig(motion.name);
            if (handConfig == null)
            {
                return;
            }

            var controller = psylliumManager.GetController(handConfig.groupIndex);
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumHand;
            var targetConfig = start.ToConfig();

            if (targetConfig.Equals(handConfig))
            {
                return;
            }

            handConfig.CopyFrom(targetConfig);
            controller.refreshRequired = true;
        }

        private void ApplyAreaMotionInit(MotionData motion, float t)
        {
            var area = psylliumManager.GetArea(motion.name);
            if (area == null)
            {
                return;
            }

            var controller = area.controller;
            if (controller == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumArea;
            var config = start.ToConfig();

            if (config.Equals(area.areaConfig))
            {
                return;
            }

            area.areaConfig = config;
            area.refreshRequired = true;
        }

        private void ApplyPatternMotionInit(MotionData motion, float t)
        {
            var patternConfig = psylliumManager.GetPatternConfig(motion.name);
            if (patternConfig == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumPattern;
            var targetConfig = start.ToConfig();

            if (targetConfig.Equals(patternConfig))
            {
                return;
            }

            patternConfig.CopyFrom(targetConfig);
        }

        private void ApplyPatternMotionUpdate(MotionData motion, float t)
        {
            var patternConfig = psylliumManager.GetPatternConfig(motion.name);
            if (patternConfig == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumPattern;
            var end = motion.end as TransformDataPsylliumPattern;

            var startConfig = start.ToConfig();
            var endConfig = end.ToConfig();

            if (startConfig.randomPositionRange != endConfig.randomPositionRange)
            {
                patternConfig.randomPositionRange = Vector3.Lerp(startConfig.randomPositionRange, endConfig.randomPositionRange, t);
            }

            if (startConfig.randomEulerAnglesRange != endConfig.randomEulerAnglesRange)
            {
                patternConfig.randomEulerAnglesRange = Vector3.Lerp(startConfig.randomEulerAnglesRange, endConfig.randomEulerAnglesRange, t);
            }
        }

        private void ApplyTransformMotionInit(MotionData motion, float t)
        {
            var transformConfig = psylliumManager.GetTransformConfig(motion.name);
            if (transformConfig == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumTransform;
            var targetConfig = start.ToConfig();

            if (targetConfig.Equals(transformConfig))
            {
                return;
            }

            transformConfig.CopyFrom(targetConfig);
        }

        private void ApplyTransformMotionUpdate(MotionData motion, float t)
        {
            var transformConfig = psylliumManager.GetTransformConfig(motion.name);
            if (transformConfig == null)
            {
                return;
            }

            var pattern = psylliumManager.GetPattern(transformConfig.groupIndex, transformConfig.patternIndex);
            if (pattern == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumTransform;
            var end = motion.end as TransformDataPsylliumTransform;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (start.position != end.position)
            {
                transformConfig.positionLeft = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.positionValues,
                    end.positionValues,
                    t);
            }

            if (start.subPosition != end.subPosition)
            {
                transformConfig.positionRight = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.subPositionValues,
                    end.subPositionValues,
                    t);
            }

            if (start.eulerAngles != end.eulerAngles)
            {
                transformConfig.eulerAnglesLeft = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.eulerAnglesValues,
                    end.eulerAnglesValues,
                    t);
            }

            if (start.subEulerAngles != end.subEulerAngles)
            {
                transformConfig.eulerAnglesRight = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.subEulerAnglesValues,
                    end.subEulerAnglesValues,
                    t);
            }
        }

        private void ApplyTempPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            var playingFrameNoFloat = this.playingFrameNoFloat;

            foreach (var pattern in psylliumManager.patterns)
            {
                var patternConfig = pattern.patternConfig;
                var transformConfig = pattern.transformConfig;
                var name = transformConfig.name;

                var timeRange = patternConfig.timeRange;
                var timeCount = patternConfig.timeCount;
                var deltaFrameNo = timeRange / timeCount * timeline.frameRate;
                var tempFrameNoFloat = playingFrameNoFloat - deltaFrameNo * timeCount * 0.5f;

                pattern.ClearTransformData();

                for (var i = 0; i < timeCount; i++)
                {
                    tempFrameNoFloat += deltaFrameNo;
                    ApplyTempPlayDataByName(name, tempFrameNoFloat);
                }
            }
        }

        private void ApplyTempPlayDataByName(
            string name,
            float playingFrameNoFloat)
        {
            var playData = _tempPlayDataMap.GetOrDefault(name);
            if (playData == null || playData.motions.Count == 0)
            {
                return;
            }

            playingFrameNoFloat = Mathf.Max(0, playingFrameNoFloat);

            var indexUpdated = playData.Update(playingFrameNoFloat);

            var current = playData.current;
            if (current != null)
            {
                ApplyTempMotion(current, playData.lerpFrame, indexUpdated);
            }
        }

        private void ApplyTempMotion(MotionData motion, float t, bool indexUpdated)
        {
            switch (motion.start.type)
            {
                case TransformType.PsylliumTransform:
                    if (indexUpdated)
                    {
                        ApplyTransformTempMotionInit(motion, t);
                    }
                    ApplyTransformTempMotionUpdate(motion, t);
                    break;
            }
        }

        private Dictionary<string, PsylliumTransformConfig> _tempTransformConfigMap = new Dictionary<string, PsylliumTransformConfig>(32);

        private void ApplyTransformTempMotionInit(MotionData motion, float t)
        {
            var transformConfig = _tempTransformConfigMap.GetOrCreate(motion.name);
            if (transformConfig == null)
            {
                return;
            }

            var start = motion.start as TransformDataPsylliumTransform;
            var targetConfig = start.ToConfig();
            transformConfig.CopyFrom(targetConfig);

            var sourceConfig = psylliumManager.GetTransformConfig(motion.name);
            transformConfig.groupIndex = sourceConfig.groupIndex;
            transformConfig.patternIndex = sourceConfig.patternIndex;
        }

        private void ApplyTransformTempMotionUpdate(MotionData motion, float t)
        {
            var transformConfig = _tempTransformConfigMap.GetOrCreate(motion.name);
            if (transformConfig == null)
            {
                return;
            }

            var pattern = psylliumManager.GetPattern(transformConfig.groupIndex, transformConfig.patternIndex);
            if (pattern == null)
            {
                MTEUtils.LogDebug("ApplyTransformTempMotionUpdate: pattern is null");
                return;
            }

            var start = motion.start as TransformDataPsylliumTransform;
            var end = motion.end as TransformDataPsylliumTransform;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (start.position != end.position)
            {
                transformConfig.positionLeft = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.positionValues,
                    end.positionValues,
                    t);
            }

            if (start.subPosition != end.subPosition)
            {
                transformConfig.positionRight = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.subPositionValues,
                    end.subPositionValues,
                    t);
            }

            if (start.eulerAngles != end.eulerAngles)
            {
                transformConfig.eulerAnglesLeft = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.eulerAnglesValues,
                    end.eulerAnglesValues,
                    t);
            }

            if (start.subEulerAngles != end.subEulerAngles)
            {
                transformConfig.eulerAnglesRight = PluginUtils.HermiteVector3(
                    t0,
                    t1,
                    start.subEulerAnglesValues,
                    end.subEulerAnglesValues,
                    t);
            }

            pattern.ApplyTransformData(transformConfig);
        }

        public void OnControllerAdded(string controllerName)
        {
            InitMenuItems();
            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }

        public void OnControllerRemoved(string controllerName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { controllerName }); // TODO: 他のボーンも削除する
            ApplyCurrentFrame(true);
        }

        public void OnAreaAdded(string areaName)
        {
            InitMenuItems();
            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }

        public void OnAreaRemoved(string areaName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { areaName });
            ApplyCurrentFrame(true);
        }

        public void OnPatternAdded(string patternName)
        {
            InitMenuItems();
            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }

        public void OnPatternRemoved(string patternName)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { patternName });
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var area in psylliumManager.areas)
            {
                if (area == null || area.transform == null)
                {
                    continue;
                }

                var areaName = area.name;

                var trans = frame.GetOrCreateTransformData<TransformDataPsylliumArea>(areaName);
                trans.FromConfig(area.areaConfig);
            }

            foreach (var pattern in psylliumManager.patterns)
            {
                if (pattern == null || pattern.controller == null)
                {
                    continue;
                }

                {
                    var patternConfig = pattern.patternConfig;
                    var patternName = patternConfig.name;

                    var trans = frame.GetOrCreateTransformData<TransformDataPsylliumPattern>(patternName);
                    trans.FromConfig(patternConfig);
                }

                {
                    var transformConfig = pattern.transformConfig;
                    var transformName = transformConfig.name;

                    var trans = frame.GetOrCreateTransformData<TransformDataPsylliumTransform>(transformName);
                    trans.FromConfig(transformConfig);
                }
            }

            foreach (var controller in psylliumManager.controllers)
            {
                if (controller == null || controller.transform == null)
                {
                    continue;
                }

                {
                    var controllerName = controller.name;

                    var trans = frame.GetOrCreateTransformData<TransformDataPsylliumController>(controllerName);
                    trans.position = controller.position;
                    trans.eulerAngles = controller.eulerAngles;
                    trans.visible = controller.visible;
                }

                {
                    var config = controller.barConfig;
                    var barConfigName = config.name;

                    var trans = frame.GetOrCreateTransformData<TransformDataPsylliumBar>(barConfigName);
                    trans.FromConfig(config);
                }

                {
                    var config = controller.handConfig;
                    var handConfigName = config.name;

                    var trans = frame.GetOrCreateTransformData<TransformDataPsylliumHand>(handConfigName);
                    trans.FromConfig(config);
                }
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            if (name.StartsWith("PsylliumController", StringComparison.Ordinal))
            {
                return TransformType.PsylliumController;
            }
            else if (name.StartsWith("PsylliumBar", StringComparison.Ordinal))
            {
                return TransformType.PsylliumBar;
            }
            else if (name.StartsWith("PsylliumHand", StringComparison.Ordinal))
            {
                return TransformType.PsylliumHand;
            }
            else if (name.StartsWith("PsylliumPattern", StringComparison.Ordinal))
            {
                return TransformType.PsylliumPattern;
            }
            else if (name.StartsWith("PsylliumTransform", StringComparison.Ordinal))
            {
                return TransformType.PsylliumTransform;
            }
            else
            {
                return TransformType.PsylliumArea;
            }
        }
    }
}