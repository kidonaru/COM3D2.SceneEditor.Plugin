using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("効果音", 51)]
    public class SeTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(SeTimelineLayer);
        public override string layerName => nameof(SeTimelineLayer);

        public static string SeBoneName = "SE";
        public static string SeDisplayName = "効果音";

        private List<string> _allBoneNames = new List<string> { SeBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private static TimelineSeManager seManager => TimelineSeManager.instance;

        private SeTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static SeTimelineLayer Create(int slotNo)
        {
            // 効果音はメイド単位ではないため常にスロット 0 の単一レイヤー
            return new SeTimelineLayer(0);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(SeBoneName, SeDisplayName);
            allMenuItems.Add(menuItem);
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
            if (indexUpdated)
            {
                var start = motion.start as TransformDataSe;
                seManager.PlaySe(start.fileName, start.interval, start.isLoop);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var trans = frame.GetOrCreateTransformData<TransformDataSe>(SeBoneName);
            trans.fileName = seManager.currentSeName;
            trans.interval = seManager.currentInterval;
            trans.isLoop = seManager.currentIsLoop;
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // 効果音の編集 UI はサウンドウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("効果音の編集はサウンドウィンドウで行ってください", -1, 20);
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Se;
        }
    }
}
