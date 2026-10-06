using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 体型スライダー (BodySliderController) をキー化するレイヤー。
    /// 項目名は BodySliderDefs のキーで、値 3 個を Tangent 補間する。
    /// BoneMenu に出してキーにするのは、体型タブで登録した項目 (TimelineData.maidBodySliderKeysMap) だけ。
    /// 登録時に 0F へキーを打ち、解除時にその項目のキーを全部消す (シェイプキーと同じ)。
    /// 骨への書き込みはコントローラーが TBody.LateUpdate の直後に行い、このレイヤーは値を渡すだけ
    /// </summary>
    [TimelineLayerDesc("体型", 19, TimelineLayerCategory.Maid)]
    public class BodySliderTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BodySliderTimelineLayer);
        public override string layerName => nameof(BodySliderTimelineLayer);

        public override bool hasSlotNo => true;

        /// <summary>登録した項目を定義順に並べたもの。登録が変わると InitMenuItems が作り直す</summary>
        private List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = OrderByDefinition(timeline.GetMaidBodySliderKeys(slotNo));
                }
                return _allBoneNames;
            }
        }

        private static BodySliderController bodySliderController
            => MaidManipulateManager.instance.bodySliderController;

        private BodySliderTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static BodySliderTimelineLayer Create(int slotNo)
        {
            return new BodySliderTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            _allBoneNames = OrderByDefinition(timeline.GetMaidBodySliderKeys(slotNo));
            foreach (var key in _allBoneNames)
            {
                _allMenuItems.Add(new BoneMenuItem(key, BodySliderDefs.Find(key).displayName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        /// <summary>登録した項目を定義順に並べる。定義に無い名前は捨てる</summary>
        public static List<string> OrderByDefinition(ICollection<string> registered)
        {
            var result = new List<string>();
            foreach (var item in BodySliderDefs.items)
            {
                if (registered.Contains(item.key))
                {
                    result.Add(item.key);
                }
            }
            return result;
        }

        /// <summary>体型タブで項目を登録した。BoneMenu に出し、0F に今の値のキーを打つ</summary>
        public void OnBodySliderKeyAdded(string key)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { key });
            ApplyCurrentFrame(true);
        }

        /// <summary>体型タブで項目の登録を外した。BoneMenu から消し、その項目のキーを全部消す</summary>
        public void OnBodySliderKeyRemoved(string key)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { key });
            ApplyCurrentFrame(true);
        }

        /// <summary>PrependDefaultFirstRows が補う 0F の既定値の行を持つ。keyFrames には入れないので保存されない</summary>
        private FrameData _defaultFirstFrame = null;

        protected override void BuildTimelineBonesMap()
        {
            base.BuildTimelineBonesMap();

            if (_defaultFirstFrame == null)
            {
                _defaultFirstFrame = CreateFrame(0);
            }
            PrependDefaultFirstRows(_timelineBonesMap, _defaultFirstFrame);
        }

        /// <summary>
        /// 最初のキーが 0F より後の項目に、0F の既定値の行を補う。
        /// 旧 XML・MaidScale からの移行・0F のキーの個別削除では、登録項目でも 0F の行を持たないことがある。
        /// 基底は最初のキーより前の区間を何もしないため、補わないとその区間の値が直前の再生・シーク次第で変わる
        /// </summary>
        public static void PrependDefaultFirstRows(Dictionary<string, List<BoneData>> rowsMap, FrameData firstFrame)
        {
            foreach (var pair in rowsMap)
            {
                var rows = pair.Value;
                if (rows.Count == 0 || rows[0].frameNo <= firstFrame.frameNo || BodySliderDefs.Find(pair.Key) == null)
                {
                    continue;
                }

                var bone = firstFrame.GetBone(pair.Key);
                if (bone == null)
                {
                    // Initialize が項目の既定値で作る
                    var trans = TimelineManager.CreateTransform<TransformDataBodySlider>(pair.Key);
                    bone = firstFrame.CreateBone(trans);
                    firstFrame.SetBone(bone);
                }
                rows.Insert(0, bone);
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var maid = this.maid;
            if (maid == null || BodySliderDefs.Find(motion.name) == null)
            {
                return;
            }

            var start = motion.start as TransformDataBodySlider;
            var end = motion.end as TransformDataBodySlider;
            if (start == null || end == null)
            {
                return;
            }

            // 一度も変えていないメイドへ既定値を書いても何も変わらない
            if (!bodySliderController.HasState(maid) && start.isDefault && end.isDefault)
            {
                return;
            }

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            var values = Vector3.zero;
            for (var i = 0; i < 3; i++)
            {
                values[i] = PluginUtils.HermiteValue(t0, t1, start.values[i], end.values[i], t);
            }
            bodySliderController.SetValues(maid, motion.name, values);
        }

        /// <summary>
        /// 削除・アンロード時に、このレイヤーが動かす項目 (登録項目とキーのある項目) だけを既定値へ戻す。
        /// 未登録の項目は体型タブで入れた値のまま残す (タイムラインに関係なく効く値のため)。
        /// 誕生時の断面がある場合は、この後の断面の復元が上書きする
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var keys = new HashSet<string>(allBoneNames);
            keys.UnionWith(_timelineBonesMap.Keys);
            foreach (var key in keys)
            {
                var item = BodySliderDefs.Find(key);
                if (item != null)
                {
                    bodySliderController.SetValues(maid, key, item.defaultValues);
                }
            }
        }

        /// <summary>
        /// 未登録の項目のキーは捨てる。ペーストやテンプレートで入ると、BoneMenu に出ずチェックを外しても消せないキーになる
        /// </summary>
        public override void UpdateBones(int frameNo, IEnumerable<BoneData> bones)
        {
            var registeredBones = new List<BoneData>();
            foreach (var bone in bones)
            {
                if (allBoneNames.Contains(bone.name))
                {
                    registeredBones.Add(bone);
                }
            }
            if (registeredBones.Count > 0)
            {
                base.UpdateBones(frameNo, registeredBones);
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

            foreach (var key in allBoneNames)
            {
                var trans = CreateTransformData<TransformDataBodySlider>(key);
                trans.vector = bodySliderController.GetValues(maid, key);

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
            return TransformType.BodySlider;
        }
    }
}
