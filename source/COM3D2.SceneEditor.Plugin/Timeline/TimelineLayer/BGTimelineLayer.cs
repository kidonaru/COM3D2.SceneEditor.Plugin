using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 背景レイヤー。ボーンは固定名 1 本で、どの背景かはキーの文字列値 (TransformDataBG.bgName) が持つ
    /// </summary>
    [TimelineLayerDesc("背景", 31, TimelineLayerCategory.Background)]
    public partial class BGTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BGTimelineLayer);
        public override string layerName => nameof(BGTimelineLayer);

        public const string BGBoneName = "BG";
        private const string BGDisplayName = "背景";

        private List<string> _allBoneNames = new List<string> { BGBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private static BgMgr bgMgr => GameMain.Instance.BgMgr;

        private static GameObject bgObject => bgMgr.current_bg_object;

        private BGTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static BGTimelineLayer Create(int slotNo)
        {
            return new BGTimelineLayer(0);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();
            allMenuItems.Add(new BoneMenuItem(BGBoneName, BGDisplayName));
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (!indexUpdated)
            {
                return;
            }

            var start = motion.start as TransformDataBG;
            var bgName = start.bgName ?? string.Empty;

            try
            {
                if (bgName != bgMgr.GetBGName())
                {
                    studioHack.ChangeBackground(bgName);
                }

                studioHack.SetBackgroundVisible(timeline.isBackgroundVisible);

                if (bgObject != null)
                {
                    bgObject.transform.localPosition = start.position;
                    bgObject.transform.localEulerAngles = start.eulerAngles;
                    bgObject.transform.localScale = start.scale;
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.LogError("選択された背景が導入されていません: " + bgName);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var trans = CreateTransformData<TransformDataBG>(BGBoneName);
            trans.bgName = bgMgr.GetBGName();
            if (bgObject != null)
            {
                trans.position = bgObject.transform.localPosition;
                trans.eulerAngles = bgObject.transform.localEulerAngles;
                trans.scale = bgObject.transform.localScale;
            }

            var bone = frame.CreateBone(trans);
            frame.SetBone(bone);
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
            return TransformType.BG;
        }
    }
}
