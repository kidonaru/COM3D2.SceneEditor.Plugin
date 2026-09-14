using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("背景色", 32, TimelineLayerCategory.Background)]
    public partial class BGColorTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(BGColorTimelineLayer);
        public override string layerName => nameof(BGColorTimelineLayer);

        /// <summary>地面の実体。所有は BGGroundManager にあり、レイヤーは参照するだけ</summary>
        private static BGGround bgGround => BGGroundManager.instance.bgGround;

        public static string BGColorBoneName = "BGColor";
        public static string BGColorDisplayName = "背景色";

        public static string BGGroundColorBoneName = "BGGroundColor";
        public static string BGGroundColorDisplayName = "地面色";

        private List<string> _allBoneNames = new List<string>
        {
            BGColorBoneName,
            BGGroundColorBoneName,
        };
        public override List<string> allBoneNames => _allBoneNames;

        private static Camera camera => PluginUtils.MainCamera;

        private BGColorTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static BGColorTimelineLayer Create(int slotNo)
        {
            return new BGColorTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            BGGroundManager.instance.GetOrCreate();

            AddFirstBones(allBoneNames);
        }

        // 地面はウィンドウからも編集するため、レイヤー破棄では消さない
        // （破棄は BGGroundManager がプラグイン無効化・シーン切替で行う）

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            {
                var menuItem = new BoneMenuItem(BGColorBoneName, BGColorDisplayName);
                allMenuItems.Add(menuItem);
            }

            {
                var menuItem = new BoneMenuItem(BGGroundColorBoneName, BGGroundColorDisplayName);
                allMenuItems.Add(menuItem);
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();

            if (!SceneEditorHack.isPoseEditing)
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
            switch (motion.start.type)
            {
                case TransformType.BGColor:
                    ApplyBGColorMotionUpdate(motion, t);
                    break;
                case TransformType.BGGroundColor:
                    if (indexUpdated)
                    {
                        ApplyBGGroundColorMotionInit(motion, t);
                    }
                    ApplyBGGroundColorMotionUpdate(motion, t);
                    break;
            }
        }

        /// <summary>背景色。色だけなので区間の開始・終了を線形補間する</summary>
        private void ApplyBGColorMotionUpdate(MotionData motion, float t)
        {
            try
            {
                var start = motion.start;
                var end = motion.end;
                camera.backgroundColor = Color.Lerp(start.color, end.color, t);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>地面色の補間されない値 (表示)。区間の開始で切り替える</summary>
        private void ApplyBGGroundColorMotionInit(MotionData motion, float t)
        {
            try
            {
                var start = motion.start;

                if (bgGround != null)
                {
                    var visible = start.visible;
                    if (timeline.isGroundLinkedToBackground && !timeline.isBackgroundVisible)
                    {
                        visible = false;
                    }

                    bgGround.visible = visible;
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>地面色の補間される値。位置・広さはタンジェント、色は線形</summary>
        private void ApplyBGGroundColorMotionUpdate(MotionData motion, float t)
        {
            try
            {
                if (bgGround == null)
                {
                    return;
                }

                var start = motion.start;
                var end = motion.end;
                var t0 = motion.stFrame * timeline.frameDuration;
                var t1 = motion.edFrame * timeline.frameDuration;

                bgGround.position = PluginUtils.HermiteVector3(
                    t0, t1, start.positionValues, end.positionValues, t);
                bgGround.scale = PluginUtils.HermiteVector3(
                    t0, t1, start.scaleValues, end.scaleValues, t);
                bgGround.color = Color.Lerp(start.color, end.color, t);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            {
                var trans = frame.GetOrCreateTransformData<TransformDataBGColor>(BGColorBoneName);
                trans.color = camera.backgroundColor;
            }

            if (bgGround != null)
            {
                var trans = frame.GetOrCreateTransformData<TransformDataBGGroundColor>(BGGroundColorBoneName);
                trans.visible = bgGround.visible;
                trans.position = bgGround.position;
                trans.scale = bgGround.scale;
                trans.color = bgGround.color;
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
            if (name == BGColorBoneName)
            {
                return TransformType.BGColor;
            }
            else if (name == BGGroundColorBoneName)
            {
                return TransformType.BGGroundColor;
            }

            return TransformType.BGColor;
        }
    }
}