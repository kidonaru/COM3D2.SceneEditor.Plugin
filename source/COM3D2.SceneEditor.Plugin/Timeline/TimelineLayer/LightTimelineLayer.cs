using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("ライト", 41)]
    public class LightTimelineLayer : LightTimelineLayerBase
    {
        public override Type layerType => typeof(LightTimelineLayer);
        public override string layerName => nameof(LightTimelineLayer);

        public override List<string> allBoneNames => lightManager.lightNames;

        private LightTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static LightTimelineLayer Create(int slotNo)
        {
            return new LightTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            AddFirstBones(allBoneNames);

            StudioLightManager.onLightAdded += OnLightAdded;
            StudioLightManager.onLightRemoved += OnLightRemoved;
            StudioLightManager.onLightUpdated += OnLightUpdated;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var light in lightManager.lights)
            {
                var menuItem = new BoneMenuItem(light.name, light.displayName);
                allMenuItems.Add(menuItem);
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            StudioLightManager.onLightAdded -= OnLightAdded;
            StudioLightManager.onLightRemoved -= OnLightRemoved;
            StudioLightManager.onLightUpdated -= OnLightUpdated;
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
            var stat = lightManager.GetLight(motion.name);
            if (stat == null)
            {
                return;
            }

            var light = stat.light;
            if (light == null)
            {
                return;
            }

            var followLight = stat.followLight;
            var transform = stat.transform;
            if (followLight == null || transform == null)
            {
                return;
            }

            if (indexUpdated)
            {
                ApplyMotionInit(motion, t, stat);
            }

            ApplyMotionUpdateTangent(motion, t, stat);
        }

        private void ApplyMotionInit(MotionData motion, float t, StudioLightStat stat)
        {
            var light = stat.light;
            var followLight = stat.followLight;

            var start = motion.start as TransformDataLight;

            // SE のメインライト (index 0) はゲーム側の恒久オブジェクトのためメイド追従を適用しない
            followLight.maidSlotNo = stat.index > 0 ? start.maidSlotNo : -1;

            stat.position = start.position;
            stat.rotation = start.rotation;
            stat.visible = start.visible;

            light.color = start.color;
            light.range = start.range;
            light.intensity = start.intensity;
            light.spotAngle = start.spotAngle;
            light.shadowStrength = start.shadowStrength;
            light.shadowBias = start.shadowBias;

            lightManager.ApplyLight(stat);
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t, StudioLightStat stat)
        {
            var light = stat.light;

            var start = motion.start as TransformDataLight;
            var end = motion.end as TransformDataLight;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            stat.position = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            stat.rotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            if (timeline.isLightColorEasing)
            {
                light.color = Color.Lerp(start.color, end.color, t);
            }

            if (timeline.isLightExtraEasing)
            {
                light.range = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.rangeValue,
                    end.rangeValue,
                    t);

                light.intensity = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.intensityValue,
                    end.intensityValue,
                    t);

                light.spotAngle = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.spotAngleValue,
                    end.spotAngleValue,
                    t);

                light.shadowStrength = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.shadowStrengthValue,
                    end.shadowStrengthValue,
                    t);

                light.shadowBias = PluginUtils.HermiteValue(
                    t0,
                    t1,
                    start.shadowBiasValue,
                    end.shadowBiasValue,
                    t);
            }

            lightManager.ApplyLight(stat);
        }

        public void OnLightAdded(StudioLightStat light)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { light.name });
            ApplyCurrentFrame(true);
        }

        public void OnLightRemoved(StudioLightStat light)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { light.name });
            ApplyCurrentFrame(true);
        }

        public void OnLightUpdated(StudioLightStat light)
        {
            InitMenuItems();
            ApplyCurrentFrame(true);
        }

        public override void OnCopyLight(StudioLightStat sourceLight, StudioLightStat newLight)
        {
            var sourceLightName = sourceLight.name;
            var newLightName = newLight.name;
            foreach (var keyFrame in keyFrames)
            {
                var sourceBone = keyFrame.GetBone(sourceLightName);
                if (sourceBone == null)
                {
                    continue;
                }

                var newBone = keyFrame.GetOrCreateBone(sourceBone.transform.type, newLightName);
                newBone.transform.FromTransformData(sourceBone.transform);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var stat in lightManager.lights)
            {
                if (stat == null || stat.light == null || stat.transform == null)
                {
                    continue;
                }

                var lightName = stat.name;
                var light = stat.light;
                var followLight = stat.followLight;

                var trans = CreateTransformData<TransformDataLight>(lightName);
                trans.position = stat.position;
                trans.rotation = stat.rotation;
                trans.visible = stat.visible;
                trans.color = light.color;
                trans.range = light.range;
                trans.intensity = light.intensity;
                trans.spotAngle = light.spotAngle;
                trans.shadowStrength = light.shadowStrength;
                trans.shadowBias = light.shadowBias;
                trans.maidSlotNo = followLight.maidSlotNo;
                trans.easing = GetEasing(frame.frameNo, lightName);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        private GUIComboBox<StudioLightStat> _lightComboBox = new GUIComboBox<StudioLightStat>
        {
            getName = (stat, index) => stat.displayName,
        };

        private GUIComboBox<MaidCache> _maidComboBox = new GUIComboBox<MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            buttonSize = new Vector2(100, 20),
            contentSize = new Vector2(150, 300),
        };

        private ColorFieldCache _colorFieldValue = new ColorFieldCache("Color", false);

        private enum TabType
        {
            操作,
            管理,
        }

        private static TabType _tabType = TabType.操作;

        public override void DrawWindow(GUIView view)
        {
            _tabType = view.DrawTabs(_tabType, 50, 20);

            switch (_tabType)
            {
                case TabType.操作:
                    DrawLightEdit(view);
                    break;
                case TabType.管理:
                    DrawLightManage(view);
                    break;
            }

            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため
            // view.DrawComboBox() は呼ばない
        }
        
        public void DrawLightEdit(GUIView view)
        {
            var lights = lightManager.lights;
            if (lights.Count == 0)
            {
                view.DrawLabel("ライトが存在しません", 200, 20);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            _lightComboBox.items = lights;
            _lightComboBox.DrawButton("対象", view);

            var stat = _lightComboBox.currentItem;

            if (stat == null || stat.light == null || stat.transform == null)
            {
                view.DrawLabel("ライトを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            {
                var light = stat.light;
                var followLight = stat.followLight;
                var transform = stat.transform;
                var position = transform.localPosition;
                var initialPosition = StudioLightStat.DefaultPosition;
                var initialEulerAngles = StudioLightStat.DefaultEulerAngles;
                var initialScale = Vector3.one;
                var updateTransform = false;
                var editType = TransformEditType.全て;

                updateTransform |= view.DrawToggle(stat.displayName, stat.visible, 200, 20, newValue =>
                {
                    stat.visible = newValue;
                });

                int drawMask;
                switch (stat.type)
                {
                    case LightType.Directional:
                        drawMask = DrawMaskRotation;
                        break;
                    case LightType.Spot:
                        drawMask = DrawMaskPositonAndRotation;
                        break;
                    case LightType.Point:
                    default:
                        drawMask = DrawMaskPosition;
                        break;
                }

                if (followLight.isFollow)
                {
                    if (IsDrawTransformType(TransformDrawType.移動, editType, drawMask))
                    {
                        initialPosition = new Vector3(0f, 0f, 0f);
                        position = followLight.offset;

                        updateTransform |= view.DrawSliderValue(
                            new GUIView.SliderOption
                            {
                                label = "X",
                                labelWidth = 30,
                                min = -config.positionRange,
                                max = config.positionRange,
                                step = 0.01f,
                                defaultValue = initialPosition.x,
                                value = position.x,
                                onChanged = x => position.x = x,
                            });

                        updateTransform |= view.DrawSliderValue(
                            new GUIView.SliderOption
                            {
                                label = "Y",
                                labelWidth = 30,
                                min = -config.positionRange,
                                max = config.positionRange,
                                step = 0.01f,
                                defaultValue = initialPosition.y,
                                value = position.y,
                                onChanged = x => position.y = x,
                            });

                        updateTransform |= view.DrawSliderValue(
                            new GUIView.SliderOption
                            {
                                label = "Z",
                                labelWidth = 30,
                                min = -config.positionRange,
                                max = config.positionRange,
                                step = 0.01f,
                                defaultValue = initialPosition.z,
                                value = position.z,
                                onChanged = x => position.z = x,
                            });

                        if (updateTransform)
                        {
                            followLight.offset = position;
                        }

                        drawMask &= DrawMaskRotation;
                    }
                }

                updateTransform |= DrawTransform(
                    view,
                    transform,
                    editType,
                    drawMask,
                    stat.name,
                    initialPosition,
                    initialEulerAngles,
                    initialScale);

                updateTransform |= view.DrawColor(
                    _colorFieldValue,
                    light.color,
                    Color.white,
                    c => light.color = c);

                if (stat.type != LightType.Directional)
                {
                    updateTransform |= view.DrawSliderValue(
                        new GUIView.SliderOption
                        {
                            label = "範囲",
                            labelWidth = 30,
                            min = 0f,
                            max = 30f,
                            step = 0.1f,
                            defaultValue = 3f,
                            value = light.range,
                            onChanged = newValue => light.range = newValue,
                        });
                }

                updateTransform |= view.DrawSliderValue(
                    new GUIView.SliderOption
                    {
                        label = "強度",
                        labelWidth = 30,
                        min = 0f,
                        max = 5f,
                        step = 0.01f,
                        defaultValue = 3f,
                        value = light.intensity,
                        onChanged = newValue => light.intensity = newValue,
                    });

                if (stat.type == LightType.Spot)
                {
                    updateTransform |= view.DrawSliderValue(
                        new GUIView.SliderOption
                        {
                            label = "角度",
                            labelWidth = 30,
                            min = 0f,
                            max = 180f,
                            step = 1f,
                            defaultValue = 50f,
                            value = light.spotAngle,
                            onChanged = newValue => light.spotAngle = newValue,
                        });
                }

                if (stat.type == LightType.Directional)
                {
                    updateTransform |= view.DrawSliderValue(
                        new GUIView.SliderOption
                        {
                            label = "影濃",
                            labelWidth = 30,
                            min = 0f,
                            max = 1f,
                            step = 0.01f,
                            defaultValue = 0.1f,
                            value = light.shadowStrength,
                            onChanged = newValue => light.shadowStrength = newValue,
                        });

                    updateTransform |= view.DrawSliderValue(
                        new GUIView.SliderOption
                        {
                            label = "影距",
                            labelWidth = 30,
                            min = 0f,
                            max = 1f,
                            step = 0.01f,
                            defaultValue = 0.01f,
                            value = light.shadowBias,
                            onChanged = newValue => light.shadowBias = newValue,
                        });
                }

                if (stat.type != LightType.Directional)
                {
                    view.BeginHorizontal();
                    {
                        view.DrawLabel("追従メイド", 70, 20);

                        view.DrawToggle("", followLight.maidSlotNo >= 0, 20, 20, newValue =>
                        {
                            followLight.maidSlotNo = newValue ? _maidComboBox.currentIndex : -1;
                        });

                        _maidComboBox.items = maidManager.maidCaches;
                        _maidComboBox.onSelected = (maidCache, index) =>
                        {
                            followLight.maidSlotNo = index;
                        };
                        _maidComboBox.DrawButton(view);
                    }
                    view.EndLayout();
                }

                if (updateTransform)
                {
                    lightManager.ApplyLight(stat);
                }

                view.DrawHorizontalLine(Color.gray);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Light;
        }
    }
}