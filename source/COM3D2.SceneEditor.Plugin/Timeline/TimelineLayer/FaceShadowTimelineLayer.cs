using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドの顔 (head スロット) の影の上書き (MaidFaceRendererManager) をキー化するレイヤー。
    /// 項目は FaceShadowItems で、有効フラグは区間開始時に適用する。
    /// キーにするのは、SE が上書きしている項目と、このレイヤーに既にキーのある項目だけ。
    /// 最初のキーより前の区間では上書きを解除する
    /// </summary>
    [TimelineLayerDesc("メイド顔の影", 8, TimelineLayerCategory.Maid)]
    public class FaceShadowTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(FaceShadowTimelineLayer);
        public override string layerName => nameof(FaceShadowTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var item in FaceShadowItems.items)
                    {
                        _allBoneNames.Add(item.name);
                    }
                }
                return _allBoneNames;
            }
        }

        /// <summary>最初のキーより前の区間にいる項目。区間に入ったときだけ解除するために持つ</summary>
        private readonly HashSet<string> _beforeFirstKeyItems = new HashSet<string>();

        private static MaidFaceRendererManager faceRenderer => MaidFaceRendererManager.instance;

        private FaceShadowTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static FaceShadowTimelineLayer Create(int slotNo)
        {
            return new FaceShadowTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();
            foreach (var item in FaceShadowItems.items)
            {
                _allMenuItems.Add(new BoneMenuItem(item.name, item.displayName));
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
            _beforeFirstKeyItems.Clear();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        /// <summary>
        /// 基底は最初のキーより前 (current == null) を何もしないため、後ろの区間で付けた上書きが
        /// シークで戻しても残る。その区間に入ったときの 1 回だけ上書きを外す
        /// (毎フレーム外すと、その区間で UI から付けた上書きまで消える)
        /// </summary>
        protected override void ApplyPlayData()
        {
            base.ApplyPlayData();

            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            foreach (var pair in _playDataMap)
            {
                if (pair.Value.current != null)
                {
                    _beforeFirstKeyItems.Remove(pair.Key);
                    continue;
                }
                if (_beforeFirstKeyItems.Add(pair.Key))
                {
                    faceRenderer.ClearOverride(maid, pair.Key);
                }
            }
        }

        /// <summary>
        /// キーにする項目と値。上書きのある項目はその値、上書きは無いがレイヤーにキーのある項目は今の値。
        /// 後者が無いと、途中で「解除」した項目のキーが作られず、前のキーの上書きが続く。並びは定義順
        /// </summary>
        public static List<KeyValuePair<string, bool>> BuildKeys(
            IDictionary<string, bool> overrides, ICollection<string> keyedItems, Func<string, bool> isEnabled)
        {
            var result = new List<KeyValuePair<string, bool>>();
            foreach (var item in FaceShadowItems.items)
            {
                bool enabled;
                if (overrides.TryGetValue(item.name, out enabled))
                {
                    result.Add(new KeyValuePair<string, bool>(item.name, enabled));
                }
                else if (keyedItems.Contains(item.name))
                {
                    result.Add(new KeyValuePair<string, bool>(item.name, isEnabled(item.name)));
                }
            }
            return result;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || !indexUpdated || FaceShadowItems.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataFaceShadow;
            if (start == null)
            {
                return;
            }

            bool current;
            if (faceRenderer.TryGetOverride(maid, motion.name, out current) && current == start.isEnabled)
            {
                return;
            }
            faceRenderer.SetOverride(maid, motion.name, start.isEnabled);
        }

        /// <summary>上書きはこのレイヤーと表情ウィンドウの影タブからしか作れないので、削除・アンロード時は全解除する</summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid != null)
            {
                faceRenderer.ClearAll(maid);
            }
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

            var keys = BuildKeys(
                faceRenderer.GetOverrides(maid),
                _playDataMap.Keys,
                item => faceRenderer.IsEnabled(maid, item));
            foreach (var pair in keys)
            {
                var trans = CreateTransformData<TransformDataFaceShadow>(pair.Key);
                trans.isEnabled = pair.Value;

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
            return TransformType.FaceShadow;
        }
    }
}
