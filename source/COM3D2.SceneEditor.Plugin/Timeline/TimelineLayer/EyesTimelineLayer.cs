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

        public override bool isDragging => _isEyesDragging;

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
            var maid = this.maid;
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

        private Texture2D _eyesPositionTex = null;
        private Texture2D _eyesTex = null;
        private bool _isEyesDragging = false;

        private void InitTexture()
        {
            if (_eyesPositionTex == null)
            {
                _eyesPositionTex = new Texture2D(150, 150);
                TextureUtils.ClearTexture(_eyesPositionTex, config.curveBgColor);

                var color1 = config.curveLineColor;
                var color2 = new Color(color1.r, color1.g, color1.b, color1.a * 0.5f);

                // 10x10のグリッドの描画
                for (var i = 1; i < 10; i++)
                {
                    var x = i * 15;
                    TextureUtils.DrawLineTexture(_eyesPositionTex, x, 0, x, 150, color2);
                    TextureUtils.DrawLineTexture(_eyesPositionTex, 0, x, 150, x, color2);
                }

                // 円の描画
                TextureUtils.DrawCircleLineTexture(
                    _eyesPositionTex,
                    150 / 2 - 0.5f,
                    64,
                    config.curveLineColor);
            }

            if (_eyesTex == null)
            {
                _eyesTex = TextureUtils.CreateCircleTexture(
                    config.frameWidth,
                    Color.white);
            }
        }

        private enum TabType
        {
            視線,
            位置,
        }

        private static TabType _tabType = TabType.視線;

        public override void DrawWindow(GUIView view)
        {
            _tabType = view.DrawTabs(_tabType, 50, 20);

            switch (_tabType)
            {
                case TabType.視線:
                    DrawEyesLookAt(view);
                    break;
                case TabType.位置:
                    DrawEyesPos(view);
                    break;
            }

            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため
            // view.DrawComboBox() は呼ばない
        }

        private void DrawEyesLookAt(GUIView view)
        {
            // 視線の編集 UI は SE の表情ウィンドウ (視線タブ) に委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("視線の編集は 表情ウィンドウの視線タブで行ってください", -1, 20);
        }

        private void DrawEyesPos(GUIView view)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            InitTexture();

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var basePos = view.currentPos;

            DrawEyesImage(view, basePos, new MotionEyesType[]
            {
                MotionEyesType.EyesPosL,
                MotionEyesType.EyesPosR,
            });

            view.currentPos = basePos;
            view.currentPos.x += 150 + 10;

            if (view.DrawButton("初期化", 60, 20))
            {
                ApplyEyes(MotionEyesType.EyesPosL, 0, 0);
                ApplyEyes(MotionEyesType.EyesPosR, 0, 0);
                ApplyEyes(MotionEyesType.EyesScaL, 0, 0);
                ApplyEyes(MotionEyesType.EyesScaR, 0, 0);
            }

            view.currentPos = basePos;
            view.currentPos.y += 150;

            var eyesTypes = new MotionEyesType[]
            {
                MotionEyesType.EyesPosL,
                MotionEyesType.EyesPosR,
                MotionEyesType.EyesScaL,
                MotionEyesType.EyesScaR,
            };

            foreach (var eyesType in eyesTypes)
            {
                DrawEyesSlider(view, eyesType);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawEyesSlider(GUIView view, MotionEyesType eyesType)
        {
            var eyesName = eyesType.ToString();
            var displayName = EyesDisplayNameMap[eyesName];

            var eyesValue = GetEyesValue(eyesType);
            var horizon = eyesValue.x;
            var vertical = eyesValue.y;
            var updateTransform = false;

            view.DrawLabel(displayName, 100, 20);

            string[] names;
            switch (eyesType)
            {
                case MotionEyesType.EyesPosL:
                case MotionEyesType.EyesPosR:
                    names = new string[] { "水平", "垂直" };
                    break;
                case MotionEyesType.EyesScaL:
                case MotionEyesType.EyesScaR:
                    names = new string[] { "幅", "高さ" };
                    break;
                default:
                    return;
            }

            updateTransform |= view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = names[0],
                    labelWidth = 30,
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = horizon,
                    onChanged = x => horizon = x,
                });

            updateTransform |= view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = names[1],
                    labelWidth = 30,
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = vertical,
                    onChanged = y => vertical = y,
                });

            if (updateTransform)
            {
                ApplyEyes(eyesType, horizon, vertical);
            }
        }

        private void DrawEyesImage(GUIView view, Vector2 basePos, MotionEyesType[] eyesTypes)
        {
            view.DrawTexture(
                _eyesPositionTex,
                150,
                150,
                studioHackManager.isPoseEditing ? Color.white : Color.gray,
                EventType.MouseDrag,
                pos =>
                {
                    _isEyesDragging = true;

                    var x = pos.x - 75;
                    var y = pos.y - 75;

                    var horizon = x / 75f;
                    var vertical = y / 75f;

                    foreach (var eyesType in eyesTypes)
                    {
                        if (eyesType == MotionEyesType.EyesPosL)
                        {
                            ApplyEyes(eyesType, -horizon, -vertical);
                        }
                        else
                        {
                            ApplyEyes(eyesType, horizon, vertical);
                        }
                    }
                });

            if (_isEyesDragging && !Input.GetMouseButton(0))
            {
                _isEyesDragging = false;
            }

            var halfEyesSize = _eyesTex.width / 2;

            Func<MotionEyesType, Vector2> getImagePosition = eyesType =>
            {
                var eyesValue = GetEyesValue(eyesType);
                var horizon = eyesValue.x;
                var vertical = eyesValue.y;

                if (eyesType == MotionEyesType.EyesPosL)
                {
                    horizon = -horizon;
                    vertical = -vertical;
                }

                var pos = new Vector2(75 + horizon * 75, 75 + vertical * 75);
                pos.x = Mathf.Clamp(pos.x, 0, 150);
                pos.y = Mathf.Clamp(pos.y, 0, 150);

                return basePos + pos - new Vector2(halfEyesSize, halfEyesSize);
            };

            foreach (var eyesType in eyesTypes)
            {
                view.currentPos = getImagePosition(eyesType);
                view.DrawTexture(_eyesTex);
            }
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