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
    /// キーにするのは既定値でない項目と、このレイヤーに既にキーのある項目だけ
    /// (全項目を毎回キーにすると XML とキー一覧が膨らむため)。
    /// 骨への書き込みはコントローラーが TBody.LateUpdate の直後に行い、このレイヤーは値を渡すだけ
    /// </summary>
    [TimelineLayerDesc("体型", 19, TimelineLayerCategory.Maid)]
    public class BodySliderTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BodySliderTimelineLayer);
        public override string layerName => nameof(BodySliderTimelineLayer);

        public override bool hasSlotNo => true;

        private static List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    _allBoneNames = new List<string>();
                    foreach (var item in BodySliderDefs.items)
                    {
                        _allBoneNames.Add(item.key);
                    }
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

            foreach (var item in BodySliderDefs.items)
            {
                _allMenuItems.Add(new BoneMenuItem(item.key, item.displayName));
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

        /// <summary>
        /// キーにする項目名。既定値でない項目と、レイヤーに既にキーのある項目。並びは定義順。
        /// 後者が無いと、途中で既定値へ戻した項目のキーが作られず、前のキーの値が続く
        /// </summary>
        public static List<string> BuildKeyNames(ICollection<string> nonDefaultKeys, ICollection<string> keyedNames)
        {
            var result = new List<string>();
            foreach (var item in BodySliderDefs.items)
            {
                if (nonDefaultKeys.Contains(item.key) || keyedNames.Contains(item.key))
                {
                    result.Add(item.key);
                }
            }
            return result;
        }

        public void OnBodySliderKeyAdded(string key)
        {
        }

        public void OnBodySliderKeyRemoved(string key)
        {
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
        /// キーは既定値でない項目だけに打つので、途中のフレームで初めて変えた項目は 0F の行を持たない。
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
        /// 値はこのレイヤーと体型タブからしか編集できないので、削除・アンロード時は既定値へ戻す。
        /// 誕生時の断面がある場合は、この後の断面の復元が上書きする
        /// </summary>
        public override void ResetOnRemove()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }
            bodySliderController.ResetAll(maid);
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

            var nonDefaultKeys = new HashSet<string>();
            foreach (var pair in bodySliderController.GetNonDefaultValues(maid))
            {
                nonDefaultKeys.Add(pair.Key);
            }

            // _playDataMap はキーを全部消した項目も残すので、行のある項目だけを持つ _timelineBonesMap で判定する
            foreach (var key in BuildKeyNames(nonDefaultKeys, _timelineBonesMap.Keys))
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
