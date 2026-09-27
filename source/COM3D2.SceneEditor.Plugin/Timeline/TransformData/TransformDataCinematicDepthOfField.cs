using System.Collections.Generic;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// シネマティック被写界深度のキーフレーム値。
    /// メイド追従は被写界深度と同じくスロット番号 1 値 (-1 = 追従なし) で持ち、
    /// 共有 DTO の maidFocus + maidIndex とはアクセサで相互変換する
    /// </summary>
    public class TransformDataCinematicDepthOfField : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            Visible = 1,

            TweakMode = 2,
            FilteringQuality = 3,
            ApertureShape = 4,
            ApertureOrientation = 5,

            FocusFocusPlane = 6,
            FocusRange = 7,
            FocusNearPlane = 8,
            FocusNearFalloff = 9,
            FocusFarPlane = 10,
            FocusFarFalloff = 11,
            FocusNearBlurRadius = 12,
            FocusFarBlurRadius = 13,

            AntiFlicker = 14,
            UseBokehTexture = 15,
            BokehScale = 16,
            BokehIntensity = 17,
            BokehThreshold = 18,
            BokehSpawnHeuristic = 19,

            MaidSlotNo = 20,
        }

        // CinematicDepthOfFieldEffect.TweakMode の値
        public const int TweakModeRange = 0;
        public const int TweakModeExplicit = 1;

        public static TransformDataCinematicDepthOfField defaultTrans = new TransformDataCinematicDepthOfField();

        public override TransformType type => TransformType.CinematicDepthOfField;

        public override int valueCount => 21;

        public override bool hasVisible => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換のためだけに残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => values;

        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

        public TransformDataCinematicDepthOfField()
        {
        }

        private static CustomValueInfo Toggle(Index index, string name, float defaultValue)
        {
            // bool は 0/1 の 2 値スライダーとして持つ (ブルームと同じ作法)
            return new CustomValueInfo
            {
                index = (int)index,
                name = name,
                min = 0f,
                max = 1f,
                step = 1f,
                defaultValue = defaultValue,
            };
        }

        /// <summary>0〜max の数値。step が 1 だと Int 扱いで補間されなくなる (CustomValueInfo.type)</summary>
        private static CustomValueInfo Value(Index index, string name, float max, float step, float defaultValue)
        {
            return new CustomValueInfo
            {
                index = (int)index,
                name = name,
                min = 0f,
                max = max,
                step = step,
                defaultValue = defaultValue,
            };
        }

        // 範囲と既定値は PostEffects 側 UI (CinematicDepthOfFieldController.DrawContent) に合わせる。
        // ただし絞りの向きは PostEffects 側の step 1 のままだと補間されないため 0.1 にしてある
        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            // 0=ピント面と範囲 / 1=近景・遠景を個別。2 値なのでトグルとして扱う
            { "tweakMode", Toggle(Index.TweakMode, "近景遠景個別", TweakModeExplicit) },
            // 0=Low / 1=Medium / 2=High
            { "filteringQuality", Value(Index.FilteringQuality, "品質", 2f, 1f, 2f) },
            // 0=円形 / 1=六角形 / 2=八角形
            { "apertureShape", Value(Index.ApertureShape, "絞りの形", 2f, 1f, 0f) },
            { "apertureOrientation", Value(Index.ApertureOrientation, "絞りの向き", 180f, 0.1f, 0f) },

            { "focusFocusPlane", Value(Index.FocusFocusPlane, "ﾋﾟﾝﾄ面", 60f, 0.1f, 20f) },
            { "focusRange", Value(Index.FocusRange, "ﾋﾟﾝﾄ範囲", 50f, 0.1f, 35f) },
            { "focusNearPlane", Value(Index.FocusNearPlane, "近景の境界", 60f, 0.1f, 3f) },
            { "focusNearFalloff", Value(Index.FocusNearFalloff, "近景の減衰", 60f, 0.1f, 3f) },
            { "focusFarPlane", Value(Index.FocusFarPlane, "遠景の境界", 60f, 0.1f, 6f) },
            { "focusFarFalloff", Value(Index.FocusFarFalloff, "遠景の減衰", 60f, 0.1f, 6f) },
            { "focusNearBlurRadius", Value(Index.FocusNearBlurRadius, "近景ぼけ半径", 100f, 0.1f, 18f) },
            { "focusFarBlurRadius", Value(Index.FocusFarBlurRadius, "遠景ぼけ半径", 100f, 0.1f, 20f) },

            { "antiFlicker", Toggle(Index.AntiFlicker, "ちらつき対策", 0f) },
            { "useBokehTexture", Toggle(Index.UseBokehTexture, "ﾃｸｽﾁｬﾎﾞｹ", 0f) },
            { "bokehScale", Value(Index.BokehScale, "ﾎﾞｹの大きさ", 20f, 0.01f, 1f) },
            { "bokehIntensity", Value(Index.BokehIntensity, "ﾎﾞｹの強さ", 400f, 0.1f, 50f) },
            { "bokehThreshold", Value(Index.BokehThreshold, "ﾎﾞｹしきい値", 5f, 0.01f, 2f) },
            { "bokehSpawnHeuristic", Value(Index.BokehSpawnHeuristic, "ﾎﾞｹ発生率", 1f, 0.01f, 0.15f) },

            {
                "maidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.MaidSlotNo,
                    name = "追従",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // ValueData アクセサ
        public ValueData tweakModeValue => values[(int)Index.TweakMode];
        public ValueData filteringQualityValue => values[(int)Index.FilteringQuality];
        public ValueData apertureShapeValue => values[(int)Index.ApertureShape];
        public ValueData apertureOrientationValue => values[(int)Index.ApertureOrientation];
        public ValueData focusFocusPlaneValue => values[(int)Index.FocusFocusPlane];
        public ValueData focusRangeValue => values[(int)Index.FocusRange];
        public ValueData focusNearPlaneValue => values[(int)Index.FocusNearPlane];
        public ValueData focusNearFalloffValue => values[(int)Index.FocusNearFalloff];
        public ValueData focusFarPlaneValue => values[(int)Index.FocusFarPlane];
        public ValueData focusFarFalloffValue => values[(int)Index.FocusFarFalloff];
        public ValueData focusNearBlurRadiusValue => values[(int)Index.FocusNearBlurRadius];
        public ValueData focusFarBlurRadiusValue => values[(int)Index.FocusFarBlurRadius];
        public ValueData antiFlickerValue => values[(int)Index.AntiFlicker];
        public ValueData useBokehTextureValue => values[(int)Index.UseBokehTexture];
        public ValueData bokehScaleValue => values[(int)Index.BokehScale];
        public ValueData bokehIntensityValue => values[(int)Index.BokehIntensity];
        public ValueData bokehThresholdValue => values[(int)Index.BokehThreshold];
        public ValueData bokehSpawnHeuristicValue => values[(int)Index.BokehSpawnHeuristic];
        public ValueData maidSlotNoValue => values[(int)Index.MaidSlotNo];

        // CustomValueInfo アクセサ
        public CustomValueInfo tweakModeInfo => GetCustomValueInfo("tweakMode");
        public CustomValueInfo filteringQualityInfo => GetCustomValueInfo("filteringQuality");
        public CustomValueInfo apertureShapeInfo => GetCustomValueInfo("apertureShape");
        public CustomValueInfo apertureOrientationInfo => GetCustomValueInfo("apertureOrientation");
        public CustomValueInfo focusFocusPlaneInfo => GetCustomValueInfo("focusFocusPlane");
        public CustomValueInfo focusRangeInfo => GetCustomValueInfo("focusRange");
        public CustomValueInfo focusNearPlaneInfo => GetCustomValueInfo("focusNearPlane");
        public CustomValueInfo focusNearFalloffInfo => GetCustomValueInfo("focusNearFalloff");
        public CustomValueInfo focusFarPlaneInfo => GetCustomValueInfo("focusFarPlane");
        public CustomValueInfo focusFarFalloffInfo => GetCustomValueInfo("focusFarFalloff");
        public CustomValueInfo focusNearBlurRadiusInfo => GetCustomValueInfo("focusNearBlurRadius");
        public CustomValueInfo focusFarBlurRadiusInfo => GetCustomValueInfo("focusFarBlurRadius");
        public CustomValueInfo antiFlickerInfo => GetCustomValueInfo("antiFlicker");
        public CustomValueInfo useBokehTextureInfo => GetCustomValueInfo("useBokehTexture");
        public CustomValueInfo bokehScaleInfo => GetCustomValueInfo("bokehScale");
        public CustomValueInfo bokehIntensityInfo => GetCustomValueInfo("bokehIntensity");
        public CustomValueInfo bokehThresholdInfo => GetCustomValueInfo("bokehThreshold");
        public CustomValueInfo bokehSpawnHeuristicInfo => GetCustomValueInfo("bokehSpawnHeuristic");
        public CustomValueInfo maidSlotNoInfo => GetCustomValueInfo("maidSlotNo");

        // 値アクセサ
        public int tweakMode
        {
            get => tweakModeValue.intValue;
            set => tweakModeValue.intValue = value;
        }

        public int filteringQuality
        {
            get => filteringQualityValue.intValue;
            set => filteringQualityValue.intValue = value;
        }

        public int apertureShape
        {
            get => apertureShapeValue.intValue;
            set => apertureShapeValue.intValue = value;
        }

        public float apertureOrientation
        {
            get => apertureOrientationValue.value;
            set => apertureOrientationValue.value = value;
        }

        public float focusFocusPlane
        {
            get => focusFocusPlaneValue.value;
            set => focusFocusPlaneValue.value = value;
        }

        public float focusRange
        {
            get => focusRangeValue.value;
            set => focusRangeValue.value = value;
        }

        public float focusNearPlane
        {
            get => focusNearPlaneValue.value;
            set => focusNearPlaneValue.value = value;
        }

        public float focusNearFalloff
        {
            get => focusNearFalloffValue.value;
            set => focusNearFalloffValue.value = value;
        }

        public float focusFarPlane
        {
            get => focusFarPlaneValue.value;
            set => focusFarPlaneValue.value = value;
        }

        public float focusFarFalloff
        {
            get => focusFarFalloffValue.value;
            set => focusFarFalloffValue.value = value;
        }

        public float focusNearBlurRadius
        {
            get => focusNearBlurRadiusValue.value;
            set => focusNearBlurRadiusValue.value = value;
        }

        public float focusFarBlurRadius
        {
            get => focusFarBlurRadiusValue.value;
            set => focusFarBlurRadiusValue.value = value;
        }

        public bool antiFlicker
        {
            get => antiFlickerValue.boolValue;
            set => antiFlickerValue.boolValue = value;
        }

        public bool useBokehTexture
        {
            get => useBokehTextureValue.boolValue;
            set => useBokehTextureValue.boolValue = value;
        }

        public float bokehScale
        {
            get => bokehScaleValue.value;
            set => bokehScaleValue.value = value;
        }

        public float bokehIntensity
        {
            get => bokehIntensityValue.value;
            set => bokehIntensityValue.value = value;
        }

        public float bokehThreshold
        {
            get => bokehThresholdValue.value;
            set => bokehThresholdValue.value = value;
        }

        public float bokehSpawnHeuristic
        {
            get => bokehSpawnHeuristicValue.value;
            set => bokehSpawnHeuristicValue.value = value;
        }

        public int maidSlotNo
        {
            get => maidSlotNoValue.intValue;
            set => maidSlotNoValue.intValue = value;
        }

        public PEP.CinematicDepthOfFieldData cinematicDepthOfField
        {
            get => new PEP.CinematicDepthOfFieldData
            {
                enabled = visible,
                tweakMode = tweakMode,
                filteringQuality = filteringQuality,
                apertureShape = apertureShape,
                apertureOrientation = apertureOrientation,
                focusFocusPlane = focusFocusPlane,
                focusRange = focusRange,
                focusNearPlane = focusNearPlane,
                focusNearFalloff = focusNearFalloff,
                focusFarPlane = focusFarPlane,
                focusFarFalloff = focusFarFalloff,
                focusNearBlurRadius = focusNearBlurRadius,
                focusFarBlurRadius = focusFarBlurRadius,
                antiFlicker = antiFlicker,
                useBokehTexture = useBokehTexture,
                bokehScale = bokehScale,
                bokehIntensity = bokehIntensity,
                bokehThreshold = bokehThreshold,
                bokehSpawnHeuristic = bokehSpawnHeuristic,
                // 被写界深度 (PostEffectManager.ApplyDepthOfField) と同じ規約で分ける
                maidFocus = maidSlotNo >= 0,
                maidIndex = maidSlotNo >= 0 ? maidSlotNo : 0,
            };
            set
            {
                visible = value.enabled;
                tweakMode = value.tweakMode;
                filteringQuality = value.filteringQuality;
                apertureShape = value.apertureShape;
                apertureOrientation = value.apertureOrientation;
                focusFocusPlane = value.focusFocusPlane;
                focusRange = value.focusRange;
                focusNearPlane = value.focusNearPlane;
                focusNearFalloff = value.focusNearFalloff;
                focusFarPlane = value.focusFarPlane;
                focusFarFalloff = value.focusFarFalloff;
                focusNearBlurRadius = value.focusNearBlurRadius;
                focusFarBlurRadius = value.focusFarBlurRadius;
                antiFlicker = value.antiFlicker;
                useBokehTexture = value.useBokehTexture;
                bokehScale = value.bokehScale;
                bokehIntensity = value.bokehIntensity;
                bokehThreshold = value.bokehThreshold;
                bokehSpawnHeuristic = value.bokehSpawnHeuristic;
                maidSlotNo = value.maidFocus ? value.maidIndex : -1;
            }
        }
    }
}
