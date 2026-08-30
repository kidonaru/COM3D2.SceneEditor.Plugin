using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("モデル", 21)]
    public class ModelTimelineLayer : ModelTimelineLayerBase
    {
        public override Type layerType => typeof(ModelTimelineLayer);
        public override string layerName => nameof(ModelTimelineLayer);

        public override List<string> allBoneNames => modelManager.modelNames;

        private ModelTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ModelTimelineLayer Create(int slotNo)
        {
            return new ModelTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            StudioModelManager.onModelAdded += OnModelAdded;
            StudioModelManager.onModelRemoved += OnModelRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var model in modelManager.models)
            {
                var menuItem = new BoneMenuItem(model.name, model.displayName);
                allMenuItems.Add(menuItem);
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            StudioModelManager.onModelAdded -= OnModelAdded;
            StudioModelManager.onModelRemoved -= OnModelRemoved;
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var model = modelManager.GetModel(motion.name);
            if (model == null || model.transform == null)
            {
                return;
            }

            if (indexUpdated)
            {
                ApplyMotionInit(motion, t, model);
            }

            ApplyMotionUpdateTangent(motion, t, model);
        }

        private void ApplyMotionInit(MotionData motion, float t, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;

            transform.localPosition = start.position;
            transform.localRotation = start.rotation;
            transform.localScale = start.scale;

            modelManager.SetModelVisible(model, start.visible && modelManager.Visible);
            model.visible = start.visible;
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;
            var end = motion.end;
            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            transform.localPosition = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            transform.localRotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
        }

        public void OnModelAdded(StudioModelStat model)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { model.name });
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { model.name });
            ApplyCurrentFrame(true);
        }

        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var sourceModelName = sourceModel.name;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                var sourceBone = keyFrame.GetBone(sourceModelName);
                if (sourceBone == null)
                {
                    continue;
                }

                var newBone = keyFrame.GetOrCreateBone(sourceBone.transform.type, newModelName);
                newBone.transform.FromTransformData(sourceBone.transform);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var model in modelManager.models)
            {
                var modelName = model.name;

                var trans = CreateTransformData<TransformDataModel>(modelName);
                trans.position = model.transform.localPosition;
                trans.rotation = model.transform.localRotation;
                trans.scale = model.transform.localScale;
                trans.visible = model.visible;

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
            return TransformType.Model;
        }
    }
}