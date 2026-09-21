using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataLight : TransformDataBase
    {
        public enum Index
        {
            PositionX = 0,
            PositionY = 1,
            PositionZ = 2,
            RotationX = 3,
            RotationY = 4,
            RotationZ = 5,
            RotationW = 6,
            ColorR = 7,
            ColorG = 8,
            ColorB = 9,
            Easing = 10,
            Range = 11,
            Intensity = 12,
            SpotAngle = 13,
            ShadowStrength = 14,
            ShadowBias = 15,
            MaidSlotNo = 16,
            Visible = 17,
            LightTarget = 18
        }

        public override TransformType type => TransformType.Light;

        public override int valueCount => 19;

        public override bool hasPosition => true;
        public override bool hasRotation => true;
        public override bool hasVisible => true;
        // Tangent 統一により常に Tangent 補間 (isTangentLight は XML 互換で残るのみ)
        public override bool hasTangent => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { 
                values[(int)Index.PositionX], 
                values[(int)Index.PositionY], 
                values[(int)Index.PositionZ] 
            };
        }

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

        public override ValueData[] tangentValues => valuesWithoutColors;

        public TransformDataLight()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "range", new CustomValueInfo
                {
                    index = (int)Index.Range,
                    name = "範囲",
                    defaultValue = 3f,
                }
            },
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "強度",
                    defaultValue = 0.95f,
                }
            },
            {
                "spotAngle", new CustomValueInfo
                {
                    index = (int)Index.SpotAngle,
                    name = "角度",
                    defaultValue = 50f,
                }
            },
            {
                "shadowStrength", new CustomValueInfo
                {
                    index = (int)Index.ShadowStrength,
                    name = "影濃",
                    defaultValue = 0.1f,
                }
            },
            {
                "shadowBias", new CustomValueInfo
                {
                    index = (int)Index.ShadowBias,
                    name = "影距",
                    defaultValue = 0.01f,
                }
            },
            {
                "maidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.MaidSlotNo,
                    name = "追従",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
            {
                "lightTarget", new CustomValueInfo
                {
                    index = (int)Index.LightTarget,
                    name = "対象",
                    // 0=全て / 1=キャラのみ / 2=背景のみ (LightTargetMode)。実体は int なので丸めて渡す
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
        };

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgb("色", (int)Index.ColorR, Color.white) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // 値アクセサ
        public ValueData rangeValue => values[(int)Index.Range];
        public ValueData intensityValue => values[(int)Index.Intensity];
        public ValueData spotAngleValue => values[(int)Index.SpotAngle];
        public ValueData shadowStrengthValue => values[(int)Index.ShadowStrength];
        public ValueData shadowBiasValue => values[(int)Index.ShadowBias];
        public ValueData maidSlotNoValue => values[(int)Index.MaidSlotNo];
        public ValueData lightTargetValue => values[(int)Index.LightTarget];

        // プロパティアクセサ
        public float range
        {
            get => rangeValue.value;
            set => rangeValue.value = value;
        }

        public float intensity
        {
            get => intensityValue.value;
            set => intensityValue.value = value;
        }

        public float spotAngle
        {
            get => spotAngleValue.value;
            set => spotAngleValue.value = value;
        }

        public float shadowStrength
        {
            get => shadowStrengthValue.value;
            set => shadowStrengthValue.value = value;
        }

        public float shadowBias
        {
            get => shadowBiasValue.value;
            set => shadowBiasValue.value = value;
        }

        public int maidSlotNo
        {
            get => maidSlotNoValue.intValue;
            set => maidSlotNoValue.intValue = value;
        }

        public int lightTarget
        {
            get => lightTargetValue.intValue;
            set => lightTargetValue.intValue = value;
        }
    }
}