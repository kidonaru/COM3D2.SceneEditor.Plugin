using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドのノード表示 (MaidNodeVisibilityController) をキー化するレイヤー。
    /// 項目名は body のボーン名 (MaidNodeVisibilityNodes) で、表示フラグは区間開始時に適用する。
    /// キーにするのは、SE が上書きしているノードと、このレイヤーに既にキーのあるノードだけ
    /// （menu が消したノードまで上書きにしないため）。最初のキーより前の区間では上書きを解除する
    /// </summary>
    [TimelineLayerDesc("メイドノード表示", 9, TimelineLayerCategory.Maid)]
    public class NodeVisibilityTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(NodeVisibilityTimelineLayer);
        public override string layerName => nameof(NodeVisibilityTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var node in MaidNodeVisibilityNodes.nodes)
                    {
                        _allBoneNames.Add(node.boneName);
                    }
                }
                return _allBoneNames;
            }
        }

        /// <summary>最初のキーより前の区間にいるノード。区間に入ったときだけ解除するために持つ</summary>
        private readonly HashSet<string> _beforeFirstKeyNodes = new HashSet<string>();

        private NodeVisibilityTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static NodeVisibilityTimelineLayer Create(int slotNo)
        {
            return new NodeVisibilityTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                _allMenuItems.Add(new BoneMenuItem(node.boneName, node.displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        /// <summary>キーの打ち直しで区間が変わるので、「区間に入った」の記録も作り直す</summary>
        protected override void BuildPlayData()
        {
            base.BuildPlayData();
            _beforeFirstKeyNodes.Clear();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
                // キーの切り替わりで複数ノードが同時に変わってもメッシュの作り直しは 1 回にする
                MaidNodeVisibilityController.Flush(maid);
            }
        }

        /// <summary>
        /// 基底は最初のキーより前 (current == null) を何もしないため、
        /// 後ろの区間で付けた上書きがシークで戻しても残る。その区間に入ったときにゲームが決めた表示へ戻す。
        /// 毎フレーム戻すと、その区間で UI から付けた上書きまで消えるので、入ったときの 1 回だけにする
        /// </summary>
        protected override void ApplyPlayData()
        {
            base.ApplyPlayData();

            var maid = this.maid;
            if (!MaidNodeVisibilityController.IsBodyReady(maid))
            {
                return;
            }
            foreach (var pair in _playDataMap)
            {
                if (pair.Value.current != null)
                {
                    _beforeFirstKeyNodes.Remove(pair.Key);
                    continue;
                }
                if (_beforeFirstKeyNodes.Add(pair.Key))
                {
                    MaidNodeVisibilityController.ClearOverride(maid, pair.Key);
                }
            }
        }

        /// <summary>
        /// キーにするノードと値。上書きのあるノードはその値、上書きは無いがレイヤーにキーのあるノードは今の表示。
        /// 後者が無いと、途中で「解除」したノードのキーが作られず、前のキーの上書きが続く。並びは定義順
        /// </summary>
        public static List<KeyValuePair<string, bool>> BuildKeys(
            IDictionary<string, bool> overrides, ICollection<string> keyedNodes, Func<string, bool> isVisible)
        {
            var result = new List<KeyValuePair<string, bool>>();
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                bool visible;
                if (overrides.TryGetValue(node.boneName, out visible))
                {
                    result.Add(new KeyValuePair<string, bool>(node.boneName, visible));
                }
                else if (keyedNodes.Contains(node.boneName))
                {
                    result.Add(new KeyValuePair<string, bool>(node.boneName, isVisible(node.boneName)));
                }
            }
            return result;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || !indexUpdated || MaidNodeVisibilityNodes.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataNodeVisibility;
            if (start == null)
            {
                return;
            }

            bool current;
            if (MaidNodeVisibilityController.TryGetOverride(maid, motion.name, out current)
                && current == start.isVisible)
            {
                return;
            }
            MaidNodeVisibilityController.SetOverride(maid, motion.name, start.isVisible);
        }

        /// <summary>
        /// 上書きはこのレイヤーとノード表示タブからしか作れないので、削除・アンロード時は全解除する。
        /// 誕生時の断面がある場合は、この後の断面の復元が上書きする
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            MaidNodeVisibilityController.ClearAll(maid);
            MaidNodeVisibilityController.Flush(maid);
        }

        public override void OnPoseEditEnd()
        {
            base.OnPoseEditEnd();
            ApplyPlayData();
            MaidNodeVisibilityController.Flush(maid);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            var keys = BuildKeys(
                MaidNodeVisibilityController.GetOverrides(maid),
                _playDataMap.Keys,
                node => MaidNodeVisibilityController.IsVisible(maid, node));
            foreach (var pair in keys)
            {
                var trans = CreateTransformData<TransformDataNodeVisibility>(pair.Key);
                trans.isVisible = pair.Value;

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
            return TransformType.NodeVisibility;
        }
    }
}
