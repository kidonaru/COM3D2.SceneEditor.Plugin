using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド表情", 10)]
    public class MorphTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MorphTimelineLayer);
        public override string layerName => nameof(MorphTimelineLayer);

        public override bool hasSlotNo => true;

        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
        {
            get
            {
                var maid = this.maid;
                return maid != null ? FaceEditManager.instance.FindStore(maid) : null;
            }
        }

        protected override List<string> trackedCandidateNames => FaceMorphUtils.saveMorphNames;

        protected override string trackedHistoryPrefix => "表情";

        private static TimelineFaceManager faceManager => TimelineFaceManager.instance;

        private Dictionary<string, float> _applyMorphMap = new Dictionary<string, float>();

        /// <summary>現在まばたきを抑止しているメイド。未抑止なら null</summary>
        private Maid _mabatakiSuppressedMaid;

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

            var targetNames = new HashSet<string>(allBoneNames);
            var setMenuItemMap = new Dictionary<string, BoneSetMenuItem>(10);

            foreach (var pair in FaceMorphUtils.morphNameToSetNameMap)
            {
                var morphName = pair.Key;
                var morphSetName = pair.Value;

                // 対象外 (未チェックかつキーフレーム未記載) のモーフは行を出さない
                if (!targetNames.Contains(morphName))
                {
                    continue;
                }

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
            else
            {
                // ポーズ編集中は適用を止めるため、まばたきの操作を SE 側へ返す
                UpdateMabatakiSuppression(null);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            UpdateMabatakiSuppression(null);
        }

        public override void OnPluginDisable()
        {
            base.OnPluginDisable();
            UpdateMabatakiSuppression(null);
        }

        /// <summary>
        /// まばたき抑止の対象を差し替える (null で解除)。
        /// 実体の書き換えと復元は SE 側コントローラが行う
        /// </summary>
        private void UpdateMabatakiSuppression(Maid maid)
        {
            // Unity の fake-null (破棄済みメイド) でも解除を呼び、SE 側の退避エントリを掃除させる
            if (!ReferenceEquals(_mabatakiSuppressedMaid, null) && !ReferenceEquals(_mabatakiSuppressedMaid, maid))
            {
                faceManager.SetMabatakiSuppressed(_mabatakiSuppressedMaid, false);
            }
            _mabatakiSuppressedMaid = maid;

            if (maid != null)
            {
                // 抑止中もゲーム側が boMabataki を立て直すため毎フレーム呼ぶ
                faceManager.SetMabatakiSuppressed(maid, true);
            }
        }

        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                UpdateMabatakiSuppression(null);
                return;
            }

            _applyMorphMap.Clear();

            base.ApplyPlayData();

            UpdateMabatakiSuppression(maid);
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

        public override void DrawWindow(GUIView view)
        {
            if (maid == null)
            {
                view.DrawLabel("メイドを配置してください", -1, 20);
                return;
            }

            // 表情モーフの編集・追跡チェックは SE の表情ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("表情の編集は表情ウィンドウで行ってください", -1, 20);
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
