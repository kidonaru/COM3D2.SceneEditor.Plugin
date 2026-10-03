using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 腕の骨の均一倍率 (MaidScaleController) をキー化するレイヤー。嘘パース用。
    /// 項目名は骨名 (MaidScaleBones) で、倍率は Tangent 補間する。
    /// 骨への書き込みはコントローラーが TBody.LateUpdate の直後に行い、このレイヤーは倍率を渡すだけ
    /// </summary>
    [TimelineLayerDesc("メイドスケール", 19, TimelineLayerCategory.Maid)]
    public class MaidScaleTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MaidScaleTimelineLayer);
        public override string layerName => nameof(MaidScaleTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var bone in MaidScaleBones.bones)
                    {
                        _allBoneNames.Add(bone.boneName);
                    }
                }
                return _allBoneNames;
            }
        }

        private static MaidScaleController scaleController
            => MaidManipulateManager.instance.maidScaleController;

        private MaidScaleTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MaidScaleTimelineLayer Create(int slotNo)
        {
            return new MaidScaleTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var bone in MaidScaleBones.bones)
            {
                _allMenuItems.Add(new BoneMenuItem(bone.boneName, bone.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
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
            var maid = this.maid;
            if (maid == null || MaidScaleBones.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataMaidScale;
            var end = motion.end as TransformDataMaidScale;
            if (start == null || end == null)
            {
                return;
            }

            // 一度も拡縮していないメイドへ元の大きさを書いても何も変わらない
            if (!scaleController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var multiplier = PluginUtils.HermiteValue(
                motion.stFrame * timeline.frameDuration,
                motion.edFrame * timeline.frameDuration,
                start.multiplierValue,
                end.multiplierValue,
                t);
            scaleController.SetScale(maid, motion.name, multiplier);
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

            foreach (var bone in MaidScaleBones.bones)
            {
                var trans = CreateTransformData<TransformDataMaidScale>(bone.boneName);
                trans.multiplier = scaleController.GetScale(maid, bone.boneName);

                var frameBone = frame.CreateBone(trans);
                frame.UpdateBone(frameBone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.MaidScale;
        }
    }
}
