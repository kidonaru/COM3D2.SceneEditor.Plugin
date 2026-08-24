using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド移動", 11)]
    public partial class MoveTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MoveTimelineLayer);
        public override string layerName => nameof(MoveTimelineLayer);

        public override bool hasSlotNo => true;
        public override bool isMoveLayer => true;

        public static string MoveBoneName = "move";
        public static string MoveDisplayName = "移動";

        private List<string> _allBoneNames = new List<string> { MoveBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private MoveTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MoveTimelineLayer Create(int slotNo)
        {
            return new MoveTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(MoveBoneName, MoveDisplayName);
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
            if (indexUpdated)
            {
                ApplyMotionInit(motion, t);
            }

            ApplyMotionUpdateTangent(motion, t);
        }

        private void ApplyMotionInit(MotionData motion, float t)
        {
            var transform = maid.transform;
            var start = motion.start;

            transform.localPosition = start.position;
            transform.localRotation = Quaternion.Euler(start.eulerAngles);
            transform.localScale = start.scale;
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t)
        {
            var transform = maid.transform;

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

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            var trans = CreateTransformData<TransformDataMove>(MoveBoneName);
            trans.position = maid.transform.localPosition;
            trans.rotation = maid.transform.localRotation;
            trans.scale = maid.transform.localScale;
            trans.easing = GetEasing(frame.frameNo, MoveBoneName);

            var bone = frame.CreateBone(trans);
            frame.UpdateBone(bone);
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var transform = maid.transform;
            var initialPosition = Vector3.zero;
            var initialEulerAngles = Vector3.zero;
            var initialScale = Vector3.one;

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            DrawTransform(
                view,
                transform,
                TransformEditType.全て,
                DrawMaskAll,
                MoveBoneName,
                initialPosition,
                initialEulerAngles,
                initialScale);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Move;
        }
    }
}