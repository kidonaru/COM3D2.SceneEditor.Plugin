// ポストエフェクトの値クラスは PostEffects.Plugin 側の実体を使う。alias の理由は PostEffectsBridge を参照
extern alias PostEffectsPlugin;
using System.Collections.Generic;
using UnityEngine;
using PEP = PostEffectsPlugin::COM3D25.PostEffects.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataRimlight : TransformDataBase
    {
        public enum Index
        {
            EulerX = 0,
            EulerY = 1,
            EulerZ = 2,
            ColorR = 3,
            ColorG = 4,
            ColorB = 5,
            ColorA = 6,
            SubColorR = 7,
            SubColorG = 8,
            SubColorB = 9,
            SubColorA = 10,
            Visible = 11,
            Easing = 12,
            LightArea = 13,
            FadeRange = 14,
            FadeExp = 15,
            MaskMode = 16,
            ExcludeFace = 17,
            ApplyHair = 18,
            UseNormal = 19,
            UseAdd = 20,
            UseMultiply = 21,
            UseOverlay = 22,
            UseSubstruct = 23,
            IsWorldSpace = 24
        }

        public static TransformDataRimlight defaultTrans = new TransformDataRimlight();

        public override TransformType type => TransformType.Rimlight;

        public override int valueCount => 25;

        public override bool hasEulerAngles => true;
        public override bool hasColor => true;
        public override bool hasSubColor => true;
        public override bool hasVisible => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換と
        // 集約型レイヤーの補間形状キャリアとして残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => values;

        public override ValueData[] eulerAnglesValues
        {
            get => new ValueData[] { 
                values[(int)Index.EulerX], 
                values[(int)Index.EulerY], 
                values[(int)Index.EulerZ] 
            };
        }
        public override ValueData[] colorValues
        {
            get => new ValueData[] { 
                values[(int)Index.ColorR], 
                values[(int)Index.ColorG], 
                values[(int)Index.ColorB], 
                values[(int)Index.ColorA] 
            };
        }
        public override ValueData[] subColorValues
        {
            get => new ValueData[] { 
                values[(int)Index.SubColorR], 
                values[(int)Index.SubColorG], 
                values[(int)Index.SubColorB], 
                values[(int)Index.SubColorA] 
            };
        }
        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

        public override Color initialColor => new Color(1f, 1f, 1f, 1f);
        public override Color initialSubColor => new Color(1f, 1f, 1f, 0f);

        public TransformDataRimlight()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);
            index = PostEffectUtils.GetEffectIndex(name);
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "lightArea", new CustomValueInfo
                {
                    index = (int)Index.LightArea,
                    name = "影響",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 1f,
                }
            },
            {
                "fadeRange", new CustomValueInfo
                {
                    index = (int)Index.FadeRange,
                    name = "幅",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.2f,
                }
            },
            {
                "fadeExp", new CustomValueInfo
                {
                    index = (int)Index.FadeExp,
                    name = "指数",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = 1f,
                }
            },
            {
                // 0=マスクなし / 1=キャラ除外 / 2=キャラのみ。実体は int なので丸めて渡す
                "maskMode", new CustomValueInfo
                {
                    index = (int)Index.MaskMode,
                    name = "マスク",
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 2f,
                }
            },
            {
                // bool を 0/1 で持つ。0.5 以上を true として実体へ渡す
                "excludeFace", new CustomValueInfo
                {
                    index = (int)Index.ExcludeFace,
                    name = "顔除外",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 1f,
                }
            },
            {
                "applyHair", new CustomValueInfo
                {
                    index = (int)Index.ApplyHair,
                    name = "髪に適用",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
            {
                "useNormal", new CustomValueInfo
                {
                    index = (int)Index.UseNormal,
                    name = "通常",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "useAdd", new CustomValueInfo
                {
                    index = (int)Index.UseAdd,
                    name = "加算",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.8f,
                }
            },
            {
                "useMultiply", new CustomValueInfo
                {
                    index = (int)Index.UseMultiply,
                    name = "乗算",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "useOverlay", new CustomValueInfo
                {
                    index = (int)Index.UseOverlay,
                    name = "Overlay",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "useSubstruct", new CustomValueInfo
                {
                    index = (int)Index.UseSubstruct,
                    name = "減算",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "isWorldSpace", new CustomValueInfo
                {
                    index = (int)Index.IsWorldSpace,
                    name = "ワールド空間",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // 値アクセサ
        public ValueData lightAreaValue => values[(int)Index.LightArea];
        public ValueData fadeRangeValue => values[(int)Index.FadeRange];
        public ValueData fadeExpValue => values[(int)Index.FadeExp];
        public ValueData maskModeValue => values[(int)Index.MaskMode];
        public ValueData excludeFaceValue => values[(int)Index.ExcludeFace];
        public ValueData applyHairValue => values[(int)Index.ApplyHair];
        public ValueData useNormalValue => values[(int)Index.UseNormal];
        public ValueData useAddValue => values[(int)Index.UseAdd];
        public ValueData useMultiplyValue => values[(int)Index.UseMultiply];
        public ValueData useOverlayValue => values[(int)Index.UseOverlay];
        public ValueData useSubstructValue => values[(int)Index.UseSubstruct];
        public ValueData isWorldSpaceValue => values[(int)Index.IsWorldSpace];

        // CustomValueInfoアクセサ
        public CustomValueInfo lightAreaInfo => GetCustomValueInfo("lightArea");
        public CustomValueInfo fadeRangeInfo => GetCustomValueInfo("fadeRange");
        public CustomValueInfo fadeExpInfo => GetCustomValueInfo("fadeExp");
        public CustomValueInfo maskModeInfo => GetCustomValueInfo("maskMode");
        public CustomValueInfo excludeFaceInfo => GetCustomValueInfo("excludeFace");
        public CustomValueInfo applyHairInfo => GetCustomValueInfo("applyHair");
        public CustomValueInfo useNormalInfo => GetCustomValueInfo("useNormal");
        public CustomValueInfo useAddInfo => GetCustomValueInfo("useAdd");
        public CustomValueInfo useMultiplyInfo => GetCustomValueInfo("useMultiply");
        public CustomValueInfo useOverlayInfo => GetCustomValueInfo("useOverlay");
        public CustomValueInfo useSubstructInfo => GetCustomValueInfo("useSubstruct");
        public CustomValueInfo isWorldSpaceInfo => GetCustomValueInfo("isWorldSpace");

        // プロパティアクセサ
        public float lightArea
        {
            get => lightAreaValue.value;
            set => lightAreaValue.value = value;
        }

        public float fadeRange
        {
            get => fadeRangeValue.value;
            set => fadeRangeValue.value = value;
        }

        public float fadeExp
        {
            get => fadeExpValue.value;
            set => fadeExpValue.value = value;
        }

        public float maskMode
        {
            get => maskModeValue.value;
            set => maskModeValue.value = value;
        }

        public float excludeFace
        {
            get => excludeFaceValue.value;
            set => excludeFaceValue.value = value;
        }

        public float applyHair
        {
            get => applyHairValue.value;
            set => applyHairValue.value = value;
        }

        public float useNormal
        {
            get => useNormalValue.value;
            set => useNormalValue.value = value;
        }

        public float useAdd
        {
            get => useAddValue.value;
            set => useAddValue.value = value;
        }

        public float useMultiply
        {
            get => useMultiplyValue.value;
            set => useMultiplyValue.value = value;
        }

        public float useOverlay
        {
            get => useOverlayValue.value;
            set => useOverlayValue.value = value;
        }

        public float useSubstruct
        {
            get => useSubstructValue.value;
            set => useSubstructValue.value = value;
        }

        public bool isWorldSpace
        {
            get => isWorldSpaceValue.boolValue;
            set => isWorldSpaceValue.boolValue = value;
        }

        public PEP.RimlightData rimlight
        {
            get => new PEP.RimlightData
            {
                enabled = visible,
                color1 = color,
                color2 = subColor,
                rotation = eulerAngles,
                lightArea = lightArea,
                fadeRange = fadeRange,
                fadeExp = fadeExp,
                maskMode = Mathf.RoundToInt(maskMode),
                excludeFace = excludeFace >= 0.5f,
                applyHair = applyHair >= 0.5f,
                useNormal = useNormal,
                useAdd = useAdd,
                useMultiply = useMultiply,
                useOverlay = useOverlay,
                useSubstruct = useSubstruct,
                isWorldSpace = isWorldSpace,
            };
            set
            {
                visible = value.enabled;
                color = value.color1;
                subColor = value.color2;
                eulerAngles = value.rotation;
                lightArea = value.lightArea;
                fadeRange = value.fadeRange;
                fadeExp = value.fadeExp;
                maskMode = value.maskMode;
                excludeFace = value.excludeFace ? 1f : 0f;
                applyHair = value.applyHair ? 1f : 0f;
                useNormal = value.useNormal;
                useAdd = value.useAdd;
                useMultiply = value.useMultiply;
                useOverlay = value.useOverlay;
                useSubstruct = value.useSubstruct;
                isWorldSpace = value.isWorldSpace;
            }
        }

        public int index;
    }
}