using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイド移動", 11)]
    public partial class MoveTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MoveTimelineLayer);
        public override string layerName => nameof(MoveTimelineLayer);

        public override bool hasSlotNo => true;
        public override bool isMoveLayer => true;

        public static string MoveBoneName = "move";
        public static string MoveDisplayName = "移動";

        private List<string> _allBoneNames = new List<string> { MoveBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private MoveTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MoveTimelineLayer Create(int slotNo)
        {
            return new MoveTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(MoveBoneName, MoveDisplayName);
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
                ApplyMotionInit(motion, t);
            }

            ApplyMotionUpdateTangent(motion, t);
        }

        /// <summary>
        /// 位置の適用。SE の退避 (非表示 = 遠方へ飛ばす) 中は実座標を動かすと
        /// 画面へ引き戻してしまうため、戻り先だけを更新する
        /// </summary>
        private void ApplyPosition(Vector3 position)
        {
            var manager = MaidManipulateManager.instance;
            if (!manager.IsVisible(maid))
            {
                manager.SetRestorePosition(maid, position);
                return;
            }

            maid.transform.localPosition = position;
        }

        private void ApplyMotionInit(MotionData motion, float t)
        {
            var transform = maid.transform;
            var start = motion.start;

            ApplyPosition(start.position);
            transform.localRotation = Quaternion.Euler(start.eulerAngles);
            transform.localScale = start.scale;
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t)
        {
            var transform = maid.transform;

            var start = motion.start;
            var end = motion.end;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            ApplyPosition(PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t));

            transform.localRotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
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

            var trans = CreateTransformData<TransformDataMove>(MoveBoneName);
            // 退避中は実座標が退避先 (遠方) で埋まっているため、キーには見かけ上の位置を焼く
            trans.position = MaidManipulateManager.instance.GetLogicalPosition(maid);
            trans.rotation = maid.transform.localRotation;
            trans.scale = maid.transform.localScale;

            var bone = frame.CreateBone(trans);
            frame.UpdateBone(bone);
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // メイド本体の Transform 編集は SE のインスペクタ / ギズモへ委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("メイドの移動はギズモ、またはインスペクタで編集してください", -1, 20);
            view.DrawLabel("※キーの値はローカル座標で記録されます", -1, 20);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Move;
        }
    }
}