using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public enum MotionEyesType
    {
        EyesPosL,
        EyesPosR,
        EyesScaL,
        EyesScaR,
        EyesRot,
        LookAtTarget,
    }

    [TimelineLayerDesc("メイド瞳", 12)]
    public class EyesTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(EyesTimelineLayer);
        public override string layerName => nameof(EyesTimelineLayer);

        public override bool hasSlotNo => true;

        public static readonly Dictionary<string, MotionEyesType> EyesTypeMap = new Dictionary<string, MotionEyesType>
        {
            { "EyesPosL", MotionEyesType.EyesPosL },
            { "EyesPosR", MotionEyesType.EyesPosR },
            { "EyesScaL", MotionEyesType.EyesScaL },
            { "EyesScaR", MotionEyesType.EyesScaR },
            { "EyesRot", MotionEyesType.EyesRot },
            { "LookAtTarget", MotionEyesType.LookAtTarget },
        };

        public static readonly Dictionary<string, string> EyesDisplayNameMap = new Dictionary<string, string>
        {
            { "EyesPosL", "左瞳位置" },
            { "EyesPosR", "右瞳位置" },
            { "EyesScaL", "左瞳サイズ" },
            { "EyesScaR", "右瞳サイズ" },
            // 旧「視線」(瞳回転)。顔向きへ一本化したため、キー名は互換のため
            // EyesRot のまま顔向き (lookX/lookY) として解釈する
            { "EyesRot", "顔向き" },
            { "LookAtTarget", "注視" },
        };

        private static List<string> _saveEyesNames = null;
        public static List<string> saveEyesNames
        {
            get
            {
                if (_saveEyesNames == null)
                {
                    _saveEyesNames = EyesTypeMap.Keys.ToList();
                }
                return _saveEyesNames;
            }
        }

        public override List<string> allBoneNames => saveEyesNames;

        private EyesTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static EyesTimelineLayer Create(int slotNo)
        {
            return new EyesTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            foreach (var pair in EyesDisplayNameMap)
            {
                var eyesName = pair.Key;
                var displayName = pair.Value;

                var menuItem = new BoneMenuItem(eyesName, displayName);
                _allMenuItems.Add(menuItem);
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
            if (motion.start.type == TransformType.LookAtTarget)
            {
                var start = motion.start as TransformDataLookAtTarget;

                var targetType = start.targetType;
                var targetIndex = start.targetIndex;
                var maidPointType = start.maidPointType;

                ApplyLookAtTarget(targetType, targetIndex, maidPointType);
            }
            else
            {
                var start = motion.start as TransformDataEyes;
                var end = motion.end as TransformDataEyes;

                var t0 = motion.stFrame * timeline.frameDuration;
                var t1 = motion.edFrame * timeline.frameDuration;

                float horizon = PluginUtils.HermiteValue(
                    t0, t1, start.horizonValue, end.horizonValue, t);
                float vertical = PluginUtils.HermiteValue(
                    t0, t1, start.verticalValue, end.verticalValue, t);

                var eyesType = EyesTypeMap[motion.name];
                ApplyEyes(eyesType, horizon, vertical);
            }
        }

        private void ApplyEyes(
            MotionEyesType eyesType,
            float horizon,
            float vertical)
        {
            ApplyEyes(maidCache, eyesType, horizon, vertical);
        }

        /// <summary>
        /// 瞳の位置・サイズ・顔向きの書き込み。
        /// レイヤー外 (SE の EyesPosRowDrawer) からも同じ換算で書けるよう静的にしている
        /// </summary>
        public static void ApplyEyes(
            MaidCache maidCache,
            MotionEyesType eyesType,
            float horizon,
            float vertical)
        {
            if (maidCache == null)
            {
                return;
            }

            switch (eyesType)
            {
                case MotionEyesType.EyesPosL:
                    maidCache.eyesPosL = new Vector3(0f, vertical / 100f, horizon / 100f);
                    break;
                case MotionEyesType.EyesPosR:
                    maidCache.eyesPosR = new Vector3(0f, vertical / 100f, horizon / 100f);
                    break;
                case MotionEyesType.EyesScaL:
                    maidCache.eyesScaL = new Vector3(0f, vertical, horizon);
                    break;
                case MotionEyesType.EyesScaR:
                    maidCache.eyesScaR = new Vector3(0f, vertical, horizon);
                    break;
                case MotionEyesType.EyesRot:
                    maidCache.lookDirection = new Vector2(horizon, vertical);
                    break;
            }
        }

        private void ApplyLookAtTarget(
            LookAtTargetType targetType,
            int targetIndex,
            MaidPointType maidPointType)
        {
            var maidCache = this.maidCache;
            if (maidCache == null)
            {
                return;
            }

            maidCache.lookAtTargetType = targetType;
            maidCache.lookAtTargetIndex = targetIndex;
            maidCache.lookAtMaidPointType = maidPointType;
        }

        private Vector2 GetEyesValue(MotionEyesType eyesType)
        {
            return GetEyesValue(maidCache, eyesType);
        }

        /// <summary>
        /// 瞳の位置・サイズ・顔向きの現在値。
        /// レイヤー外 (SE の EyesPosRowDrawer) からも同じ換算で読めるよう静的にしている
        /// </summary>
        public static Vector2 GetEyesValue(MaidCache maidCache, MotionEyesType eyesType)
        {
            var maid = maidCache != null ? maidCache.maid : null;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return Vector2.zero;
            }

            switch (eyesType)
            {
                case MotionEyesType.EyesPosL:
                {
                    var pos = maidCache.eyesPosL;
                    return new Vector2(pos.z * 100f, pos.y * 100f);
                }
                case MotionEyesType.EyesPosR:
                {
                    var pos = maidCache.eyesPosR;
                    return new Vector2(pos.z * 100f, pos.y * 100f);
                }
                case MotionEyesType.EyesScaL:
                {
                    var sca = maidCache.eyesScaL;
                    return new Vector2(sca.z, sca.y);
                }
                case MotionEyesType.EyesScaR:
                {
                    var sca = maidCache.eyesScaR;
                    return new Vector2(sca.z, sca.y);
                }
                case MotionEyesType.EyesRot:
                    return maidCache.lookDirection;
            }

            return Vector2.zero;
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var eyesName in allBoneNames)
            {
                var eyesType = EyesTypeMap[eyesName];

                if (eyesType == MotionEyesType.LookAtTarget)
                {
                    var trans = CreateTransformData<TransformDataLookAtTarget>(eyesName);
                    var targetType = maidCache.lookAtTargetType;
                    trans.targetType = targetType;
                    trans.targetIndex = 0;
                    trans.maidPointType = 0;

                    switch (targetType)
                    {
                        case LookAtTargetType.Camera:
                            break;
                        case LookAtTargetType.Maid:
                            trans.targetIndex = maidCache.lookAtTargetIndex;
                            trans.maidPointType = maidCache.lookAtMaidPointType;
                            break;
                        case LookAtTargetType.Model:
                            trans.targetIndex = maidCache.lookAtTargetIndex;
                            break;
                    }

                    var bone = frame.CreateBone(trans);
                    frame.UpdateBone(bone);
                }
                else
                {
                    var trans = CreateTransformData<TransformDataEyes>(eyesName);
                    var eyesValue = GetEyesValue(eyesType);
                    trans.horizon = eyesValue.x;
                    trans.vertical = eyesValue.y;

                    var bone = frame.CreateBone(trans);
                    frame.UpdateBone(bone);
                }
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            if (name == "LookAtTarget")
            {
                return TransformType.LookAtTarget;
            }
            else
            {
                return TransformType.Eyes;
            }
        }
    }
}