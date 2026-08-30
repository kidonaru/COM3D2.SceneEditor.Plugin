using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイドボイス", 14)]
    public partial class VoiceTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(VoiceTimelineLayer);
        public override string layerName => nameof(VoiceTimelineLayer);

        public override bool hasSlotNo => true;

        public static string VoiceBoneName = "Voice";
        public static string VoiceDisplayName = "ボイス";

        private List<string> _allBoneNames = new List<string> { VoiceBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private VoiceTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static VoiceTimelineLayer Create(int slotNo)
        {
            return new VoiceTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(VoiceBoneName, VoiceDisplayName);
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
                var start = motion.start as TransformDataVoice;

                maidCache.oneShotVoiceName = start.voiceName;
                maidCache.oneShotVoiceStartTime = start.startTime;
                maidCache.oneShotVoiceLength = start.length;
                maidCache.voiceFadeTime = start.fadeTime;
                maidCache.voicePitch = start.pitch;
                maidCache.loopVoiceName = start.loopVoiceName;
                maidCache.PlayOneShotVoice();
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            var trans = frame.GetOrCreateTransformData<TransformDataVoice>(VoiceBoneName);
            trans.voiceName = maidCache.oneShotVoiceName;
            trans.startTime = maidCache.oneShotVoiceStartTime;
            trans.length = maidCache.oneShotVoiceLength;
            trans.fadeTime = maidCache.voiceFadeTime;
            trans.pitch = maidCache.voicePitch;
            trans.loopVoiceName = maidCache.loopVoiceName;
        }

        public List<MotionData> GetVoiceMotionData()
        {
            return _playDataMap[VoiceBoneName].motions;
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
            return TransformType.Voice;
        }
    }
}