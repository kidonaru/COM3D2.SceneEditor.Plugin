using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド表情", 10)]
    public class MorphTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MorphTimelineLayer);
        public override string layerName => nameof(MorphTimelineLayer);

        public override bool hasSlotNo => true;

        public override List<string> allBoneNames => FaceMorphUtils.saveMorphNames;

        private static TimelineFaceManager faceManager => TimelineFaceManager.instance;

        private Dictionary<string, float> _applyMorphMap = new Dictionary<string, float>();

        // ON にするとタイムラインの値を編集中も強制的に維持する (スライダー操作が反映される)
        private bool _isForceUpdate = false;

        private MorphTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MorphTimelineLayer Create(int slotNo)
        {
            return new MorphTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var setMenuItemMap = new Dictionary<string, BoneSetMenuItem>(10);

            foreach (var pair in FaceMorphUtils.morphNameToSetNameMap)
            {
                var morphName = pair.Key;
                var morphSetName = pair.Value;

                BoneSetMenuItem setMenuItem;
                if (!setMenuItemMap.TryGetValue(morphSetName, out setMenuItem))
                {
                    var displaySetName = FaceMorphUtils.GetMorphSetJpName(morphSetName);
                    setMenuItem = new BoneSetMenuItem(morphSetName, displaySetName);
                    setMenuItemMap[morphSetName] = setMenuItem;
                    allMenuItems.Add(setMenuItem);
                }

                var displayName = FaceMorphUtils.GetMorphJpName(morphName);
                setMenuItem.AddChild(new BoneMenuItem(morphName, displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";

            var firstFrame = this.firstFrame;
            if (firstFrame == null || firstFrame.frameNo != 0)
            {
                errorMessage = "0フレーム目にキーフレームが必要です";
                return false;
            }

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

        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            _applyMorphMap.Clear();

            base.ApplyPlayData();

            faceManager.SetMabatakiOff(maid);
            faceManager.SetMorphValue(maid, _applyMorphMap);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var start = motion.start as TransformDataMorph;
            var end = motion.end as TransformDataMorph;
            var morphName = motion.name;

            if (indexUpdated)
            {
                _applyMorphMap[morphName] = start.morphValue;
            }

            if (start.morphValue != end.morphValue)
            {
                _applyMorphMap[morphName] = Lerp(start.morphValue, end.morphValue, t, morphName);
            }
        }

        /// <summary>頬・涙などのオプションモーフは中間値を持たないためステップ適用する</summary>
        private float Lerp(float startValue, float endValue, float lerpFrame, string morphName)
        {
            if (FaceMorphUtils.IsStepMorph(morphName) && lerpFrame < 0.99f)
            {
                lerpFrame = 0f;
            }
            return Mathf.Lerp(startValue, endValue, lerpFrame);
        }

        private float GetMorphValue(string morphName)
        {
            if (_isForceUpdate)
            {
                float forcedValue;
                if (_applyMorphMap.TryGetValue(morphName, out forcedValue))
                {
                    return forcedValue;
                }
                return 0f;
            }

            var morphValue = faceManager.GetMorphValue(maid, morphName);

            // ゲーム側が m_fEyeCloseRate で掛けた目閉じ補正を打ち消し、素の値へ戻す
            if (morphName == "eyeclose")
            {
                var morph = maid.body0.Face.morph;
                if (morph != null)
                {
                    var eyeCloseRate = morph.m_fEyeCloseRate;
                    if (eyeCloseRate != 0f && eyeCloseRate != 1f)
                    {
                        morphValue = (morphValue - eyeCloseRate) / (1f - eyeCloseRate);
                        morphValue = Mathf.Clamp01(morphValue);
                    }
                }
            }

            return morphValue;
        }

        private void SetMorphValue(string morphName, float value)
        {
            if (_isForceUpdate)
            {
                _applyMorphMap[morphName] = value;
                return;
            }

            faceManager.SetMorphValue(maid, new Dictionary<string, float> { { morphName, value } });
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            foreach (var name in allBoneNames)
            {
                var trans = frame.GetOrCreateTransformData<TransformDataMorph>(name);
                trans.morphValue = GetMorphValue(name);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        private enum TabType
        {
            目,
            眉,
            口,
            他,
        }

        private static TabType _tabType = TabType.目;

        public override void DrawWindow(GUIView view)
        {
            if (maid == null)
            {
                view.DrawLabel("メイドを配置してください", -1, 20);
                return;
            }

            _tabType = view.DrawTabs(_tabType, 50, 20);

            view.DrawToggle("強制上書き", _isForceUpdate, 150, 20, newValue =>
            {
                _isForceUpdate = newValue;
            });

            view.DrawHorizontalLine(Color.gray);

            // SE 版 GUIView には IsComboBoxFocused がないため focusedComboBox 判定に置き換え
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            DrawMorph(view);
        }

        private void DrawMorph(GUIView view)
        {
            view.DrawLabel(_tabType.ToString(), 80, 20);

            switch (_tabType)
            {
                case TabType.目:
                    foreach (var morphName in FaceMorphUtils.eyeMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.眉:
                    foreach (var morphName in FaceMorphUtils.mayuMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.口:
                    foreach (var morphName in FaceMorphUtils.mouthMorphJp.Keys)
                    {
                        DrawMorphSlider(view, morphName);
                    }
                    break;
                case TabType.他:
                    foreach (var morphName in FaceMorphUtils.faceOptionMorphJp.Keys)
                    {
                        DrawMorphToggle(view, morphName);
                    }
                    break;
            }

            if (studioHackManager.isPoseEditing && _isForceUpdate)
            {
                faceManager.SetMorphValue(maid, _applyMorphMap);
            }
        }

        private void DrawMorphSlider(GUIView view, string morphName)
        {
            view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = FaceMorphUtils.GetMorphJpName(morphName),
                    labelWidth = 80,
                    min = 0f,
                    max = 1f,
                    step = 0f,
                    defaultValue = 0f,
                    value = GetMorphValue(morphName),
                    onChanged = newValue => SetMorphValue(morphName, newValue),
                });
        }

        private void DrawMorphToggle(GUIView view, string morphName)
        {
            var displayName = FaceMorphUtils.GetMorphJpName(morphName);
            var isOn = GetMorphValue(morphName) >= 1f;

            view.DrawToggle(displayName, isOn, 150, 20, newValue =>
            {
                SetMorphValue(morphName, newValue ? 1f : 0f);
            });
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Morph;
        }
    }
}
