using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    [TimelineLayerDesc("モデル", 21, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
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

        public override void ResetOnRemove()
        {
            timeline.models.Clear();
            modelManager.SetupModels(timeline.models);
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

            if (!SceneEditorHack.isPoseEditing)
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

            var start = motion.start as TransformDataModel;
            var end = motion.end as TransformDataModel;
            if (start == null || end == null)
            {
                return;
            }

            var attachKey = t < StepEndThreshold ? start : end;
            var attachChanged = modelManager.ApplyAttach(
                model, attachKey.attachPoint, attachKey.attachMaidSlotNo);

            // 付け替えはローカル位置・回転を 0 に戻すので、区間頭と同じく入れ直す
            if (indexUpdated || attachChanged)
            {
                ApplyMotionInit(motion, model);
            }

            // 同値区間は ApplyMotionInit が入れた start の値のままでよい (MotionData.isConstant)
            if (motion.isConstant)
            {
                return;
            }

            // 終点側へ付け替えた後もワールド座標で書くので、区間の最後まで続けても親に依らず連続する
            if (end.worldLerp)
            {
                if (ApplyMotionWorldLerp(motion, t, model, start, end))
                {
                    return;
                }
            }

            ApplyMotionUpdateTangent(motion, t, model);
        }

        private void ApplyMotionInit(MotionData motion, StudioModelStat model)
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

        /// <summary>
        /// 始点キー (始点の親基準) と終点キー (そのフレームの終点の親基準) のワールド姿勢を線形補間する。
        /// タンジェントはローカル座標系の傾きなので使わない。親が解決できなければ false (ローカル補間へ戻す)
        /// </summary>
        private bool ApplyMotionWorldLerp(
            MotionData motion,
            float t,
            StudioModelStat model,
            TransformDataModel start,
            TransformDataModel end)
        {
            var startParent = modelManager.GetAttachParent(model, start.attachPoint, start.attachMaidSlotNo);
            var endParent = modelManager.GetAttachParent(model, end.attachPoint, end.attachMaidSlotNo);
            if (startParent == null || endParent == null)
            {
                return false;
            }

            var transform = model.transform;
            transform.position = Vector3.Lerp(
                startParent.TransformPoint(start.position),
                endParent.TransformPoint(end.position),
                t);
            transform.rotation = Quaternion.Slerp(
                startParent.rotation * start.rotation,
                endParent.rotation * end.rotation,
                t);

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
            return true;
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
            // 呼び出し元は一時フレームを渡すことが多いので、引き継ぎ元は登録済みキーから引く
            var existingFrame = GetFrame(frame.frameNo);

            foreach (var model in modelManager.models)
            {
                var modelName = model.name;

                var trans = CreateTransformData<TransformDataModel>(modelName);
                trans.position = model.transform.localPosition;
                trans.rotation = model.transform.localRotation;
                trans.scale = model.transform.localScale;
                trans.visible = model.visible;
                if (TransformDataModel.IsAttached(model.attachPoint, model.attachMaidSlotNo))
                {
                    trans.attachMaidSlotNo = model.attachMaidSlotNo;
                    trans.attachPoint = model.attachPoint;
                }
                else
                {
                    trans.SetUnattached();
                }

                var existingBone = existingFrame != null ? existingFrame.GetBone(modelName) : null;
                trans.InheritKeySettings(existingBone != null ? existingBone.transform as TransformDataModel : null);

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