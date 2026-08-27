using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("背景", 31)]
    public partial class BGTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BGTimelineLayer);
        public override string layerName => nameof(BGTimelineLayer);

        private List<string> _allBoneNames = new List<string>();
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
            {
                var boneNameSet = new HashSet<string>(GetExistBoneNames());

                var currentBgName = bgMgr.GetBGName();
                if (!string.IsNullOrEmpty(currentBgName))
                {
                    boneNameSet.Add(currentBgName);
                }

                _allBoneNames.Clear();
                _allBoneNames.AddRange(boneNameSet);
            }

            {
                allMenuItems.Clear();

                foreach (var boneName in allBoneNames)
                {
                    var displayName = photoBGManager.GetDisplayName(boneName);
                    var menuItem = new BoneMenuItem(boneName, displayName);
                    allMenuItems.Add(menuItem);
                }
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        private string _prevBgName = null;

        public override void Update()
        {
            base.Update();

            if (studioHackManager.isPoseEditing)
            {
                var bgName = bgMgr.GetBGName();
                if (bgName != _prevBgName)
                {
                    OnBGChanged();
                    _prevBgName = bgName;
                }
            }
            else
            {
                _prevBgName = null;
            }

            if (!studioHackManager.isPoseEditing)
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

            //MTEUtils.LogDebug("ApplyMotion: bgName={0} stFrame={1}, stPos={2}, stRot={3}",
            //    motion.name, motion.stFrame, motion.myTm.stPos, motion.myTm.stRot);

            try
            {
                if (motion.name != bgMgr.GetBGName())
                {
                    studioHack.ChangeBackground(motion.name);
                }

                studioHack.SetBackgroundVisible(timeline.isBackgroundVisible);

                var start = motion.start;

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
                MTEUtils.LogError("選択された背景が導入されていません: " + motion.name);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var bgName = bgMgr.GetBGName();

            var trans = CreateTransformData<TransformDataBG>(bgName);
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

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            // 背景の選択と Transform 編集は SE の BackgroundWindow に委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("背景の編集は 背景ウィンドウで行ってください", -1, 20);
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.BG;
        }

        public override void UpdateBones(int frameNo, IEnumerable<BoneData> bones)
        {
            // 背景は常に前のフレームをクリアしてから更新する
            var frame = GetOrCreateFrame(frameNo);
            frame.ClearBones();
            frame.UpdateBones(bones);
        }

        private void OnBGChanged()
        {
            InitMenuItems();
        }
    }
}