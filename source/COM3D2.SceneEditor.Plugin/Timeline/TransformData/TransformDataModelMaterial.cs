
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static COM3D2.MotionTimelineEditor.Plugin.ModelMaterial;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataModelMaterial : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            ColorR = 1,
            ColorG = 2,
            ColorB = 3,
            ColorA = 4,
            ShadowColorR = 5,
            ShadowColorG = 6,
            ShadowColorB = 7,
            ShadowColorA = 8,
            RimColorR = 9,
            RimColorG = 10,
            RimColorB = 11,
            RimColorA = 12,
            OutlineColorR = 13,
            OutlineColorG = 14,
            OutlineColorB = 15,
            OutlineColorA = 16,
            Shininess = 17,
            OutlineWidth = 18,
            RimPower = 19,
            RimShift = 20,
            EmissionColorR = 21,
            EmissionColorG = 22,
            EmissionColorB = 23,
            EmissionColorA = 24,
            MatcapColorR = 25,
            MatcapColorG = 26,
            MatcapColorB = 27,
            MatcapColorA = 28,
            MatcapMaskColorR = 29,
            MatcapMaskColorG = 30,
            MatcapMaskColorB = 31,
            MatcapMaskColorA = 32,
            RimLightColorR = 33,
            RimLightColorG = 34,
            RimLightColorB = 35,
            RimLightColorA = 36,
            NormalValue = 37,
            ParallaxValue = 38,
            MatcapValue = 39,
            MatcapMaskValue = 40,
            EmissionValue = 41,
            EmissionHDRExposure = 42,
            EmissionPower = 43,
            RimLightValue = 44,
            RimLightPower = 45,
            MetallicValue = 46,
            SmoothnessValue = 47,
            OcclusionValue = 48,
        }

        public static TransformDataModelMaterial defaultTrans = new TransformDataModelMaterial();

        public override TransformType type => TransformType.ModelMaterial;

        public override int valueCount => 49;

        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換と
        // 集約型レイヤーの補間形状キャリアとして残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;

        public override ValueData easingValue => values[(int)Index.Easing];

        public TransformDataModelMaterial()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "_Shininess",
                new CustomValueInfo
                {
                    index = (int)Index.Shininess,
                    name = "光沢度",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                }
            },
            {
                "_OutlineWidth",
                new CustomValueInfo
                {
                    index = (int)Index.OutlineWidth,
                    name = "アウトライン幅",
                    min = 0f,
                    max = 1f,
                    step = 0.0001f,
                }
            },
            {
                "_RimPower",
                new CustomValueInfo
                {
                    index = (int)Index.RimPower,
                    name = "リムパワー",
                    min = -30f,
                    max = 30f,
                    step = 0.01f,
                }
            },
            {
                "_RimShift",
                new CustomValueInfo
                {
                    index = (int)Index.RimShift,
                    name = "リムシフト",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                }
            },
            // NPRの追加プロパティ
            {
                "_NormalValue",
                new CustomValueInfo
                {
                    index = (int)Index.NormalValue,
                    name = "法線マップ強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_ParallaxValue",
                new CustomValueInfo
                {
                    index = (int)Index.ParallaxValue,
                    name = "視差効果強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_MatcapValue",
                new CustomValueInfo
                {
                    index = (int)Index.MatcapValue,
                    name = "マットキャップ強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_MatcapMaskValue",
                new CustomValueInfo
                {
                    index = (int)Index.MatcapMaskValue,
                    name = "マットキャップマスク強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_EmissionValue",
                new CustomValueInfo
                {
                    index = (int)Index.EmissionValue,
                    name = "発光強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_EmissionHDRExposure",
                new CustomValueInfo
                {
                    index = (int)Index.EmissionHDRExposure,
                    name = "発光HDR露出",
                    min = 0f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_EmissionPower",
                new CustomValueInfo
                {
                    index = (int)Index.EmissionPower,
                    name = "発光パワー",
                    min = -3f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_RimLightValue",
                new CustomValueInfo
                {
                    index = (int)Index.RimLightValue,
                    name = "リムライト強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_RimLightPower",
                new CustomValueInfo
                {
                    index = (int)Index.RimLightPower,
                    name = "リムライトパワー",
                    min = -3f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_MetallicValue",
                new CustomValueInfo
                {
                    index = (int)Index.MetallicValue,
                    name = "金属度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_SmoothnessValue",
                new CustomValueInfo
                {
                    index = (int)Index.SmoothnessValue,
                    name = "滑らかさ",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "_OcclusionValue",
                new CustomValueInfo
                {
                    index = (int)Index.OcclusionValue,
                    name = "オクルージョン",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
        };

        public const string ShadowColorKey = "ShadowColor";
        public const string RimColorKey = "RimColor";
        public const string OutlineColorKey = "OutlineColor";
        public const string EmissionColorKey = "EmissionColor";
        public const string MatcapColorKey = "MatcapColor";
        public const string MatcapMaskColorKey = "MatcapMaskColor";
        public const string RimLightColorKey = "RimLightColor";

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色", (int)Index.ColorR, Color.white) },
                { ShadowColorKey, ColorValueInfo.Rgba("影色", (int)Index.ShadowColorR, new Color(0f, 0f, 0f, 1f)) },
                { RimColorKey, ColorValueInfo.Rgba("リム色", (int)Index.RimColorR, new Color(0f, 0f, 0f, 1f)) },
                { OutlineColorKey, ColorValueInfo.Rgba("アウトライン", (int)Index.OutlineColorR, new Color(0f, 0f, 0f, 1f)) },
                { EmissionColorKey, ColorValueInfo.Rgba("発光色", (int)Index.EmissionColorR, Color.white) },
                { MatcapColorKey, ColorValueInfo.Rgba("マットキャップ色", (int)Index.MatcapColorR, Color.white) },
                { MatcapMaskColorKey, ColorValueInfo.Rgba("マットキャップマスク色", (int)Index.MatcapMaskColorR, Color.white) },
                { RimLightColorKey, ColorValueInfo.Rgba("リムライト色", (int)Index.RimLightColorR, Color.white) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData ShininessValue => values[(int)Index.Shininess];
        public ValueData OutlineWidthValue => values[(int)Index.OutlineWidth];
        public ValueData RimPowerValue => values[(int)Index.RimPower];
        public ValueData RimShiftValue => values[(int)Index.RimShift];

        public ValueData NormalValueValue => values[(int)Index.NormalValue];
        public ValueData ParallaxValueValue => values[(int)Index.ParallaxValue];
        public ValueData MatcapValueValue => values[(int)Index.MatcapValue];
        public ValueData MatcapMaskValueValue => values[(int)Index.MatcapMaskValue];
        public ValueData EmissionValueValue => values[(int)Index.EmissionValue];
        public ValueData EmissionHDRExposureValue => values[(int)Index.EmissionHDRExposure];
        public ValueData EmissionPowerValue => values[(int)Index.EmissionPower];
        public ValueData RimLightValueValue => values[(int)Index.RimLightValue];
        public ValueData RimLightPowerValue => values[(int)Index.RimLightPower];
        public ValueData MetallicValueValue => values[(int)Index.MetallicValue];
        public ValueData SmoothnessValueValue => values[(int)Index.SmoothnessValue];
        public ValueData OcclusionValueValue => values[(int)Index.OcclusionValue];

        public CustomValueInfo ShininessInfo => CustomValueInfoMap["_Shininess"];
        public CustomValueInfo OutlineWidthInfo => CustomValueInfoMap["_OutlineWidth"];
        public CustomValueInfo RimPowerInfo => CustomValueInfoMap["_RimPower"];
        public CustomValueInfo RimShiftInfo => CustomValueInfoMap["_RimShift"];

        public CustomValueInfo NormalValueInfo => CustomValueInfoMap["_NormalValue"];
        public CustomValueInfo ParallaxValueInfo => CustomValueInfoMap["_ParallaxValue"];
        public CustomValueInfo MatcapValueInfo => CustomValueInfoMap["_MatcapValue"];
        public CustomValueInfo MatcapMaskValueInfo => CustomValueInfoMap["_MatcapMaskValue"];
        public CustomValueInfo EmissionValueInfo => CustomValueInfoMap["_EmissionValue"];
        public CustomValueInfo EmissionHDRExposureInfo => CustomValueInfoMap["_EmissionHDRExposure"];
        public CustomValueInfo EmissionPowerInfo => CustomValueInfoMap["_EmissionPower"];
        public CustomValueInfo RimLightValueInfo => CustomValueInfoMap["_RimLightValue"];
        public CustomValueInfo RimLightPowerInfo => CustomValueInfoMap["_RimLightPower"];
        public CustomValueInfo MetallicValueInfo => CustomValueInfoMap["_MetallicValue"];
        public CustomValueInfo SmoothnessValueInfo => CustomValueInfoMap["_SmoothnessValue"];
        public CustomValueInfo OcclusionValueInfo => CustomValueInfoMap["_OcclusionValue"];

        public Color ShadowColor
        {
            get => GetColorValue(ShadowColorKey);
            set => SetColorValue(ShadowColorKey, value);
        }

        public Color RimColor
        {
            get => GetColorValue(RimColorKey);
            set => SetColorValue(RimColorKey, value);
        }

        public Color OutlineColor
        {
            get => GetColorValue(OutlineColorKey);
            set => SetColorValue(OutlineColorKey, value);
        }

        public float Shininess
        {
            get => ShininessValue.value;
            set => ShininessValue.value = value;
        }

        public float OutlineWidth
        {
            get => OutlineWidthValue.value;
            set => OutlineWidthValue.value = value;
        }

        public float RimPower
        {
            get => RimPowerValue.value;
            set => RimPowerValue.value = value;
        }

        public float RimShift
        {
            get => RimShiftValue.value;
            set => RimShiftValue.value = value;
        }

        public Color EmissionColor
        {
            get => GetColorValue(EmissionColorKey);
            set => SetColorValue(EmissionColorKey, value);
        }

        public Color MatcapColor
        {
            get => GetColorValue(MatcapColorKey);
            set => SetColorValue(MatcapColorKey, value);
        }

        public Color MatcapMaskColor
        {
            get => GetColorValue(MatcapMaskColorKey);
            set => SetColorValue(MatcapMaskColorKey, value);
        }

        public Color RimLightColor
        {
            get => GetColorValue(RimLightColorKey);
            set => SetColorValue(RimLightColorKey, value);
        }

        public float NormalValue
        {
            get => NormalValueValue.value;
            set => NormalValueValue.value = value;
        }
        
        public float ParallaxValue
        {
            get => ParallaxValueValue.value;
            set => ParallaxValueValue.value = value;
        }
        
        public float MatcapValue
        {
            get => MatcapValueValue.value;
            set => MatcapValueValue.value = value;
        }
        
        public float MatcapMaskValue
        {
            get => MatcapMaskValueValue.value;
            set => MatcapMaskValueValue.value = value;
        }
        
        public float EmissionValue
        {
            get => EmissionValueValue.value;
            set => EmissionValueValue.value = value;
        }
        
        public float EmissionHDRExposure
        {
            get => EmissionHDRExposureValue.value;
            set => EmissionHDRExposureValue.value = value;
        }
        
        public float EmissionPower
        {
            get => EmissionPowerValue.value;
            set => EmissionPowerValue.value = value;
        }
        
        public float RimLightValue
        {
            get => RimLightValueValue.value;
            set => RimLightValueValue.value = value;
        }
        
        public float RimLightPower
        {
            get => RimLightPowerValue.value;
            set => RimLightPowerValue.value = value;
        }
        
        public float MetallicValue
        {
            get => MetallicValueValue.value;
            set => MetallicValueValue.value = value;
        }
        
        public float SmoothnessValue
        {
            get => SmoothnessValueValue.value;
            set => SmoothnessValueValue.value = value;
        }
        
        public float OcclusionValue
        {
            get => OcclusionValueValue.value;
            set => OcclusionValueValue.value = value;
        }

        public CustomValueInfo GetCustomValueInfo(ModelMaterial.ValuePropertyType propertyType)
        {
            switch (propertyType)
            {
                case ModelMaterial.ValuePropertyType._Shininess:
                    return ShininessInfo;
                case ModelMaterial.ValuePropertyType._OutlineWidth:
                    return OutlineWidthInfo;
                case ModelMaterial.ValuePropertyType._RimPower:
                    return RimPowerInfo;
                case ModelMaterial.ValuePropertyType._RimShift:
                    return RimShiftInfo;

                // NPR用プロパティの追加
                case ModelMaterial.ValuePropertyType._NormalValue:
                    return NormalValueInfo;
                case ModelMaterial.ValuePropertyType._ParallaxValue:
                    return ParallaxValueInfo;
                case ModelMaterial.ValuePropertyType._MatcapValue:
                    return MatcapValueInfo;
                case ModelMaterial.ValuePropertyType._MatcapMaskValue:
                    return MatcapMaskValueInfo;
                case ModelMaterial.ValuePropertyType._EmissionValue:
                    return EmissionValueInfo;
                case ModelMaterial.ValuePropertyType._EmissionHDRExposure:
                    return EmissionHDRExposureInfo;
                case ModelMaterial.ValuePropertyType._EmissionPower:
                    return EmissionPowerInfo;
                case ModelMaterial.ValuePropertyType._RimLightValue:
                    return RimLightValueInfo;
                case ModelMaterial.ValuePropertyType._RimLightPower:
                    return RimLightPowerInfo;
                case ModelMaterial.ValuePropertyType._MetallicValue:
                    return MetallicValueInfo;
                case ModelMaterial.ValuePropertyType._SmoothnessValue:
                    return SmoothnessValueInfo;
                case ModelMaterial.ValuePropertyType._OcclusionValue:
                    return OcclusionValueInfo;
                default:
                    return null;
            }
        }

        public void Apply(ModelMaterial material)
        {
            color = material.GetColor(ColorPropertyType._Color);
            ShadowColor = material.GetColor(ColorPropertyType._ShadowColor);
            RimColor = material.GetColor(ColorPropertyType._RimColor);
            OutlineColor = material.GetColor(ColorPropertyType._OutlineColor);

            Shininess = material.GetValue(ValuePropertyType._Shininess);
            OutlineWidth = material.GetValue(ValuePropertyType._OutlineWidth);
            RimPower = material.GetValue(ValuePropertyType._RimPower);
            RimShift = material.GetValue(ValuePropertyType._RimShift);

            // NPR用プロパティの追加
            EmissionColor = material.GetColor(ColorPropertyType._EmissionColor);
            MatcapColor = material.GetColor(ColorPropertyType._MatcapColor);
            MatcapMaskColor = material.GetColor(ColorPropertyType._MatcapMaskColor);
            RimLightColor = material.GetColor(ColorPropertyType._RimLightColor);

            NormalValue = material.GetValue(ValuePropertyType._NormalValue);
            ParallaxValue = material.GetValue(ValuePropertyType._ParallaxValue);
            MatcapValue = material.GetValue(ValuePropertyType._MatcapValue);
            MatcapMaskValue = material.GetValue(ValuePropertyType._MatcapMaskValue);
            EmissionValue = material.GetValue(ValuePropertyType._EmissionValue);
            EmissionHDRExposure = material.GetValue(ValuePropertyType._EmissionHDRExposure);
            EmissionPower = material.GetValue(ValuePropertyType._EmissionPower);
            RimLightValue = material.GetValue(ValuePropertyType._RimLightValue);
            RimLightPower = material.GetValue(ValuePropertyType._RimLightPower);
            MetallicValue = material.GetValue(ValuePropertyType._MetallicValue);
            SmoothnessValue = material.GetValue(ValuePropertyType._SmoothnessValue);
            OcclusionValue = material.GetValue(ValuePropertyType._OcclusionValue);
        }
    }
}