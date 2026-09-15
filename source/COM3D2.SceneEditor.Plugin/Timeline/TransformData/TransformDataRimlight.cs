using System.Collections.Generic;
using UnityEngine;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataRimlight : TransformDataBase
    {
        public enum Index
        {
            RotationX = 0,
            RotationY = 1,
            RotationZ = 2,
            RotationW = 3,
            ColorR = 4,
            ColorG = 5,
            ColorB = 6,
            ColorA = 7,
            SubColorR = 8,
            SubColorG = 9,
            SubColorB = 10,
            SubColorA = 11,
            Visible = 12,
            Easing = 13,
            LightArea = 14,
            FadeRange = 15,
            FadeExp = 16,
            MaskMode = 17,
            ExcludeFace = 18,
            ApplyHair = 19,
            UseNormal = 20,
            UseAdd = 21,
            UseMultiply = 22,
            UseOverlay = 23,
            UseSubstruct = 24,
            IsWorldSpace = 25
        }

        public static TransformDataRimlight defaultTrans = new TransformDataRimlight();

        public override TransformType type => TransformType.Rimlight;

        public override int valueCount => 26;

        public override bool hasRotation => true;
        public override bool hasVisible => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換のためだけに残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;

        // 符号補正 (最短経路への寄せ) は 2 段構え。
        // キー確定時は TimelineLayerBase.FixRotation が隣接キーの内積を見て符号をそろえる。
        // これは GetAnmBinary 経由だが、GetAnmBinary は CreateAndApplyAnm からロード時に
        // 全レイヤーで呼ばれるので、.anm 書き出し時だけでなく再生データにも効く。
        // 再生時は LerpFrom が通る QuaternionUtils.Slerp が重ねて内積を見る
        public override ValueData[] rotationValues
        {
            get => new ValueData[] {
                values[(int)Index.RotationX],
                values[(int)Index.RotationY],
                values[(int)Index.RotationZ],
                values[(int)Index.RotationW]
            };
        }
        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

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

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色1", (int)Index.ColorR, new Color(1f, 1f, 1f, 1f)) },
                { ColorKey.Sub, ColorValueInfo.Rgba("色2", (int)Index.SubColorR, new Color(1f, 1f, 1f, 0f)) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

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