using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

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

        public override void DrawWindow(GUIView view)
        {
            if (maidCache == null)
            {
                view.DrawLabel("メイドを配置してください", -1, 20);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始",
                labelWidth = 30,
                min = 0f,
                max = config.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceStartTime,
                onChanged = value => maidCache.oneShotVoiceStartTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "長さ",
                labelWidth = 30,
                min = 0f,
                max = config.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceLength,
                onChanged = value => maidCache.oneShotVoiceLength = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "Fade",
                labelWidth = 30,
                min = 0f,
                max = config.voiceMaxLength,
                step = 0.01f,
                defaultValue = 0.1f,
                value = maidCache.voiceFadeTime,
                onChanged = value => maidCache.voiceFadeTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音程",
                labelWidth = 30,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = maidCache.voicePitch,
                onChanged = value => maidCache.voicePitch = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ボイス名",
                labelWidth = 75,
                value = maidCache.oneShotVoiceName,
                onChanged = value => maidCache.oneShotVoiceName = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ループボイス",
                labelWidth = 75,
                value = maidCache.loopVoiceName,
                onChanged = value => maidCache.loopVoiceName = value,
            });

            if (view.DrawButton("再生", 100, 20))
            {
                maidCache.PlayOneShotVoice();
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.DrawHorizontalLine(Color.gray);

            // ボイス一覧出力 UI は ScriptLoader (DCM 系) 未移植のため省略
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