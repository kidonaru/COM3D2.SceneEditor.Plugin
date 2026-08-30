using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("ポストエフェクト", 52)]
    public partial class PostEffectTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(PostEffectTimelineLayer);
        public override string layerName => nameof(PostEffectTimelineLayer);

        public override bool isPostEffectLayer => true;

        private List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>(
                        2 + timeline.paraffinCount + timeline.distanceFogCount + timeline.rimlightCount);
                    _allBoneNames.Add("DepthOfField");
                    _allBoneNames.Add("GTToneMap");
                    _allBoneNames.AddRange(paraffinNames);
                    _allBoneNames.AddRange(distanceFogNames);
                    _allBoneNames.AddRange(rimlightNames);
                }
                return _allBoneNames;
            }
        }

        private static PostEffectManager postEffectManager => PostEffectManager.instance;

        private PostEffectTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static PostEffectTimelineLayer Create(int slotNo)
        {
            return new PostEffectTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();
            AddFirstBones(allBoneNames);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var effectName in allBoneNames)
            {
                var jpName = PostEffectUtils.ToJpName(effectName);
                var menuItem = new BoneMenuItem(effectName, jpName);
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

            var boneCount = 2
                + timeline.paraffinCount
                + timeline.distanceFogCount
                + timeline.rimlightCount;
            if (allBoneNames.Count != boneCount)
            {
                MTEUtils.LogDebug($"PostEffetTimelineLayer.Update: boneCount={boneCount}");
                _allBoneNames = null;
                postEffectManager.InitPostEffects();
                InitMenuItems();
                AddFirstBones(allBoneNames);
            }

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

            if (!isCurrent && !config.isPostEffectSync)
            {
                postEffectManager.DisableAllEffects();
                return;
            }

            var stopwatch = new StopwatchDebug();
            ApplyPlayDataByType(TransformType.DepthOfField);
            //stopwatch.ProcessEnd("  DepthOfField");

            ApplyPlayDataByType(TransformType.Paraffin);
            //stopwatch.ProcessEnd("  Paraffin");

            ApplyPlayDataByType(TransformType.DistanceFog);
            //stopwatch.ProcessEnd("  DistanceFog");

            ApplyPlayDataByType(TransformType.Rimlight);
            //stopwatch.ProcessEnd("  Rimlight");

            ApplyPlayDataByType(TransformType.GTToneMap);
            //stopwatch.ProcessEnd("  GTToneMap");
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.DepthOfField:
                    ApplyDepthOfField(motion, t);
                    break;
                case TransformType.Paraffin:
                    ApplyParaffin(motion, t);
                    break;
                case TransformType.DistanceFog:
                    ApplyDistanceFog(motion, t);
                    break;
                case TransformType.Rimlight:
                    ApplyRimlight(motion, t);
                    break;
                case TransformType.GTToneMap:
                    ApplyGTToneMap(motion, t);
                    break;
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var effectName in allBoneNames)
            {
                var effectType = PostEffectUtils.GetEffectType(effectName);

                switch (effectType)
                {
                    case PostEffectType.DepthOfField:
                    {
                        var trans = CreateTransformData<TransformDataDepthOfField>(effectName);
                        trans.depthOfField = postEffectManager.GetDepthOfFieldData();

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
                    case PostEffectType.Paraffin:
                    {
                        var trans = CreateTransformData<TransformDataParaffin>(effectName);
                        trans.paraffin = postEffectManager.GetParaffinData(trans.index);

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
                    case PostEffectType.DistanceFog:
                    {
                        var trans = CreateTransformData<TransformDataDistanceFog>(effectName);
                        trans.distanceFog = postEffectManager.GetDistanceFogData(trans.index);

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
                    case PostEffectType.Rimlight:
                    {
                        var trans = CreateTransformData<TransformDataRimlight>(effectName);
                        trans.rimlight = postEffectManager.GetRimlightData(trans.index);

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
                    case PostEffectType.GTToneMap:
                    {
                        var trans = CreateTransformData<TransformDataGTToneMap>(effectName);
                        trans.data = postEffectManager.GetGTToneMapData();

                        var bone = frame.CreateBone(trans);
                        frame.UpdateBone(bone);
                        break;
                    }
                }
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            var effectType = PostEffectUtils.GetEffectType(name);
            switch (effectType)
            {
                case PostEffectType.DepthOfField:
                    return TransformType.DepthOfField;
                case PostEffectType.Paraffin:
                    return TransformType.Paraffin;
                case PostEffectType.DistanceFog:
                    return TransformType.DistanceFog;
                case PostEffectType.Rimlight:
                    return TransformType.Rimlight;
                case PostEffectType.GTToneMap:
                    return TransformType.GTToneMap;
            }

            return TransformType.None;
        }
    }
}