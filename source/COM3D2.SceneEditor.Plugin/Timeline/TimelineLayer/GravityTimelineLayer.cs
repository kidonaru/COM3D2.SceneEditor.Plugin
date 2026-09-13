using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 髪・スカートの重力 (MaidGravityController) をキー化するレイヤー。
    /// 項目名は GravityCategory.id ("hair" / "skirt") で、履歴・プリセットと同じキーを使う。
    /// 有効フラグは区間開始時に適用し、オフセットは Tangent 補間する
    /// </summary>
    [TimelineLayerDesc("メイド重力", 18, TimelineLayerCategory.Maid)]
    public class GravityTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(GravityTimelineLayer);
        public override string layerName => nameof(GravityTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var category in MaidGravityController.categories)
                    {
                        _allBoneNames.Add(category.id);
                    }
                }
                return _allBoneNames;
            }
        }

        private static MaidGravityController gravityController
            => MaidManipulateManager.instance.gravityController;

        private GravityTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static GravityTimelineLayer Create(int slotNo)
        {
            return new GravityTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var category in MaidGravityController.categories)
            {
                _allMenuItems.Add(new BoneMenuItem(category.id, category.name));
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

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var category = MaidGravityController.FindCategory(motion.name);
            if (category == null)
            {
                return;
            }

            var start = motion.start as TransformDataGravity;
            var end = motion.end as TransformDataGravity;

            // 重力を一度も使っていないメイドへ既定値だけを書き戻すと、
            // 何も変わらないのにコンポーネントだけが作られて常駐コストになる
            // (GravitySnapshot.Apply と同じ判定)
            if (!gravityController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            var offset = PluginUtils.HermiteVector3(
                t0, t1, start.offsetValues, end.offsetValues, t);
            gravityController.SetOffset(maid, category, offset);

            // 有効フラグは補間できないので区間の開始値をそのまま使う
            if (indexUpdated)
            {
                gravityController.SetEnabled(maid, category, start.enabled);
            }
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

            foreach (var category in MaidGravityController.categories)
            {
                var trans = CreateTransformData<TransformDataGravity>(category.id);
                trans.enabled = gravityController.GetEnabled(maid, category);
                trans.offset = gravityController.GetOffset(maid, category);

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
            return TransformType.Gravity;
        }
    }
}
