using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイドシェイプ", 13)]
    public partial class ShapeKeyTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(ShapeKeyTimelineLayer);
        public override string layerName => nameof(ShapeKeyTimelineLayer);

        public override bool hasSlotNo => true;

        private List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    var shapeKeys = timeline.GetMaidShapeKeys(slotNo);
                    _allBoneNames = new List<string>(shapeKeys);
                }

                return _allBoneNames;
            }
        }

        private ShapeKeyTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ShapeKeyTimelineLayer Create(int slotNo)
        {
            return new ShapeKeyTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            _allBoneNames = null;
            foreach (var boneName in allBoneNames)
            {
                var menuItem = new BoneMenuItem(boneName, boneName);
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
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyPlayData()
        {
            if (maidCache == null)
            {
                return;
            }

            base.ApplyPlayData();

            maidCache.FixBlendValues(_playDataMap.Keys);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var start = motion.start as TransformDataShapeKey;
            var end = motion.end as TransformDataShapeKey;

            var weight = PluginUtils.HermiteValue(
                motion.stFrame * timeline.frameDuration,
                motion.edFrame * timeline.frameDuration,
                start.weightValue,
                end.weightValue,
                t);
            maidCache.SetBlendShapeValue(motion.name, weight);
        }

        public override void OnShapeKeyAdded(string shapeKey)
        {
            InitMenuItems();

            var boneNames = new List<string> { shapeKey };
            AddFirstBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void OnShapeKeyRemoved(string shapeKey)
        {
            InitMenuItems();

            var boneNames = new List<string> { shapeKey };
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var boneName in allBoneNames)
            {
                var trans = CreateTransformData<TransformDataShapeKey>(boneName);
                trans.weight = maidCache.GetBlendShapeValue(boneName);

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
            return TransformType.ShapeKey;
        }
    }
}