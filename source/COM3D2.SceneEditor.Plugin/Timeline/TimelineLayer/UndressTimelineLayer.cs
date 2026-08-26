using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド脱衣", 16)]
    public class UndressTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(UndressTimelineLayer);
        public override string layerName => nameof(UndressTimelineLayer);

        public override bool hasSlotNo => true;

        public override List<string> allBoneNames => DressUtils.DressSlotNames;

        private UndressTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static UndressTimelineLayer Create(int slotNo)
        {
            return new UndressTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            Action<string, string, List<DressSlotID>> addCategory = (setName, setJpName, slotIds) =>
            {
                var setMenuItem = new BoneSetMenuItem(setName, setJpName);
                _allMenuItems.Add(setMenuItem);

                foreach (var slotId in slotIds)
                {
                    var displayName = DressUtils.GetDressSlotJpName(slotId);
                    var menuItem = new BoneMenuItem(slotId.ToString(), displayName);
                    setMenuItem.AddChild(menuItem);
                }
            };

            addCategory("clothing", "衣装", DressUtils.ClothingSlotIds);
            addCategory("headwear", "頭部衣装", DressUtils.HeadwearSlotIds);
            addCategory("accessory", "アクセ", DressUtils.AccessorySlotIds);
            addCategory("mekure", "めくれ", DressUtils.MekureSlotIds);
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
            var start = motion.start as TransformDataUndress;

            if (indexUpdated)
            {
                maidCache.SetSlotVisible(start.slotId, start.isVisible);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maidCache = this.maidCache;
            if (maidCache == null) return;

            foreach (var slotName in allBoneNames)
            {
                var slotId = DressUtils.GetDressSlotId(slotName);
                var trans = frame.GetOrCreateTransformData<TransformDataUndress>(slotName);
                trans.isVisible = maidCache.IsSlotVisible(slotId);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // 脱衣の編集 UI は SE の脱衣ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("脱衣の編集は脱衣ウィンドウで行ってください", -1, 20);
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Undress;
        }
    }
}