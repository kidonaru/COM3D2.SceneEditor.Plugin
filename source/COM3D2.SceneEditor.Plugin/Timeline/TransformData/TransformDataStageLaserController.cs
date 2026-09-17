using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataStageLaserController : TransformDataBase
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
            ColorA = 10,
            SubColorR = 11,
            SubColorG = 12,
            SubColorB = 13,
            SubColorA = 14,
            Visible = 15,
            Intensity = 16,
            LaserRange = 17,
            LaserWidth = 18,
            FalloffExp = 19,
            NoiseStrength = 20,
            NoiseScale = 21,
            CoreRadius = 22,
            OffsetRange = 23,
            GlowWidth = 24,
            SegmentRange = 25,
            AutoPosition = 26,
            AutoRotation = 27,
            AutoColor = 28,
            AutoLaserInfo = 29,
            AutoVisible = 30,
            ZTest = 31,
            RotationMinX = 32,
            RotationMinY = 33,
            RotationMinZ = 34,
            RotationMaxX = 35,
            RotationMaxY = 36,
            RotationMaxZ = 37
        }

        public static TransformDataStageLaserController defaultTrans = new TransformDataStageLaserController();

        public override TransformType type => TransformType.StageLaserController;

        public override int valueCount => 38;

        public override bool hasPosition => true;
        public override bool hasRotation => true;
        public override bool hasVisible => true;
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

        private List<ValueData> _tangentValues = null;
        public override ValueData[] tangentValues
        {
            get
            {
                if (_tangentValues == null)
                {
                    _tangentValues = new List<ValueData>();
                    _tangentValues.AddRange(positionValues);
                    _tangentValues.AddRange(rotationValues);
                    _tangentValues.AddRange(rotationMinValues);
                    _tangentValues.AddRange(rotationMaxValues);
                    _tangentValues.AddRange(new ValueData[] { 
                        values[(int)Index.Intensity], 
                        values[(int)Index.LaserRange], 
                        values[(int)Index.LaserWidth] 
                    });
                }
                return _tangentValues.ToArray();
            }
        }

        public override Vector3 initialPosition => StageLaserController.DefaultPosition;
        public override Vector3 initialEulerAngles => StageLaserController.DefaultEulerAngles;
        public override Quaternion initialRotation
            => QuaternionUtils.EulerToQuaternion(initialEulerAngles);
        public Vector3 initialRotationMin => new Vector3(0f, 40f, 0f);
        public Vector3 initialRotationMax => new Vector3(0f, -40f, 0f);

        public TransformDataStageLaserController()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 1f,
                }
            },
            {
                "laserRange", new CustomValueInfo
                {
                    index = (int)Index.LaserRange,
                    name = "範囲",
                    min = 0f,
                    max = 100f,
                    step = 0.1f,
                    defaultValue = 13f,
                }
            },
            {
                "laserWidth", new CustomValueInfo
                {
                    index = (int)Index.LaserWidth,
                    name = "幅",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.05f,
                }
            },
            {
                "falloffExp", new CustomValueInfo
                {
                    index = (int)Index.FalloffExp,
                    name = "減衰指数",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = 0.2f,
                }
            },
            {
                "noiseStrength", new CustomValueInfo
                {
                    index = (int)Index.NoiseStrength,
                    name = "ﾉｲｽﾞ強度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0.1f,
                }
            },
            {
                "noiseScale", new CustomValueInfo
                {
                    index = (int)Index.NoiseScale,
                    name = "ﾉｲｽﾞｻｲｽﾞ",
                    min = 1f,
                    max = 100f,
                    step = 0.1f,
                    defaultValue = 10f,
                }
            },
            {
                "coreRadius", new CustomValueInfo
                {
                    index = (int)Index.CoreRadius,
                    name = "中心半径",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "offsetRange", new CustomValueInfo
                {
                    index = (int)Index.OffsetRange,
                    name = "ｵﾌｾｯﾄ範囲",
                    min = 0f,
                    max = 10f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "glowWidth", new CustomValueInfo
                {
                    index = (int)Index.GlowWidth,
                    name = "散乱幅",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = 0.1f,
                }
            },
            {
                "segmentRange", new CustomValueInfo
                {
                    index = (int)Index.SegmentRange,
                    name = "分割範囲",
                    min = 1,
                    max = 64,
                    step = 1,
                    defaultValue = 10,
                }
            },
            {
                "autoPosition", new CustomValueInfo // 未使用
                {
                    index = (int)Index.AutoPosition,
                    name = "一括位置",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1f,
                }
            },
            {
                "autoRotation", new CustomValueInfo
                {
                    index = (int)Index.AutoRotation,
                    name = "一括回転",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1f,
                }
            },
            {
                "autoColor", new CustomValueInfo
                {
                    index = (int)Index.AutoColor,
                    name = "一括色",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1f,
                }
            },
            {
                "autoLaserInfo", new CustomValueInfo
                {
                    index = (int)Index.AutoLaserInfo,
                    name = "一括情報",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1f,
                }
            },
            {
                "autoVisible", new CustomValueInfo
                {
                    index = (int)Index.AutoVisible,
                    name = "一括表示",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1f,
                }
            },
            {
                "zTest", new CustomValueInfo
                {
                    index = (int)Index.ZTest,
                    name = "Zテスト",
                    min = 0,
                    max = 1,
                    step = 1,
                    defaultValue = 1,
                }
            },
            {
                "rotationMinX", new CustomValueInfo
                {
                    index = (int)Index.RotationMinX,
                    name = "最小RX",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "rotationMinY", new CustomValueInfo
                {
                    index = (int)Index.RotationMinY,
                    name = "最小RY",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "rotationMinZ", new CustomValueInfo
                {
                    index = (int)Index.RotationMinZ,
                    name = "最小RZ",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "rotationMaxX", new CustomValueInfo
                {
                    index = (int)Index.RotationMaxX,
                    name = "最大RX",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "rotationMaxY", new CustomValueInfo
                {
                    index = (int)Index.RotationMaxY,
                    name = "最大RY",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
            {
                "rotationMaxZ", new CustomValueInfo
                {
                    index = (int)Index.RotationMaxZ,
                    name = "最大RZ",
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                }
            },
        };

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("中心色", (int)Index.ColorR, StageLaser.DefaultColor1) },
                { ColorKey.Sub, ColorValueInfo.Rgba("錯乱色", (int)Index.SubColorR, StageLaser.DefaultColor2) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // 値アクセサ
        public ValueData intensityValue => values[(int)Index.Intensity];
        public ValueData laserRangeValue => values[(int)Index.LaserRange];
        public ValueData laserWidthValue => values[(int)Index.LaserWidth];
        public ValueData falloffExpValue => values[(int)Index.FalloffExp];
        public ValueData noiseStrengthValue => values[(int)Index.NoiseStrength];
        public ValueData noiseScaleValue => values[(int)Index.NoiseScale];
        public ValueData coreRadiusValue => values[(int)Index.CoreRadius];
        public ValueData offsetRangeValue => values[(int)Index.OffsetRange];
        public ValueData glowWidthValue => values[(int)Index.GlowWidth];
        public ValueData segmentRangeValue => values[(int)Index.SegmentRange];
        public ValueData autoRotationValue => values[(int)Index.AutoRotation];
        public ValueData autoColorValue => values[(int)Index.AutoColor];
        public ValueData autoLaserInfoValue => values[(int)Index.AutoLaserInfo];
        public ValueData autoVisibleValue => values[(int)Index.AutoVisible];
        public ValueData zTestValue => values[(int)Index.ZTest];
        /// <summary>
        /// 自動回転の振れ幅。姿勢ではなく軸ごとに独立した値なので、version 35 の
        /// クォータニオン化の対象から外してオイラー角のまま残している
        /// (クォータニオンにすると「X 軸だけ振る」が表現できなくなるため)
        /// </summary>
        public ValueData[] rotationMinValues => new ValueData[] { 
            values[(int)Index.RotationMinX], 
            values[(int)Index.RotationMinY], 
            values[(int)Index.RotationMinZ] 
        };
        /// <inheritdoc cref="rotationMinValues"/>
        public ValueData[] rotationMaxValues => new ValueData[] { 
            values[(int)Index.RotationMaxX], 
            values[(int)Index.RotationMaxY], 
            values[(int)Index.RotationMaxZ] 
        };

        // CustomValueInfoアクセサ
        public CustomValueInfo intensityInfo => CustomValueInfoMap["intensity"];
        public CustomValueInfo laserRangeInfo => CustomValueInfoMap["laserRange"];
        public CustomValueInfo laserWidthInfo => CustomValueInfoMap["laserWidth"];
        public CustomValueInfo falloffExpInfo => CustomValueInfoMap["falloffExp"];
        public CustomValueInfo noiseStrengthInfo => CustomValueInfoMap["noiseStrength"];
        public CustomValueInfo noiseScaleInfo => CustomValueInfoMap["noiseScale"];
        public CustomValueInfo coreRadiusInfo => CustomValueInfoMap["coreRadius"];
        public CustomValueInfo offsetRangeInfo => CustomValueInfoMap["offsetRange"];
        public CustomValueInfo glowWidthInfo => CustomValueInfoMap["glowWidth"];
        public CustomValueInfo segmentRangeInfo => CustomValueInfoMap["segmentRange"];
        public CustomValueInfo autoRotationInfo => CustomValueInfoMap["autoRotation"];
        public CustomValueInfo autoColorInfo => CustomValueInfoMap["autoColor"];
        public CustomValueInfo autoLaserInfoInfo => CustomValueInfoMap["autoLaserInfo"];
        public CustomValueInfo autoVisibleInfo => CustomValueInfoMap["autoVisible"];
        public CustomValueInfo zTestInfo => CustomValueInfoMap["zTest"];

        // プロパティアクセサ
        public float intensity
        {
            get => intensityValue.value;
            set => intensityValue.value = value;
        }

        public float laserRange
        {
            get => laserRangeValue.value;
            set => laserRangeValue.value = value;
        }

        public float laserWidth
        {
            get => laserWidthValue.value;
            set => laserWidthValue.value = value;
        }

        public float falloffExp
        {
            get => falloffExpValue.value;
            set => falloffExpValue.value = value;
        }

        public float noiseStrength
        {
            get => noiseStrengthValue.value;
            set => noiseStrengthValue.value = value;
        }

        public float noiseScale
        {
            get => noiseScaleValue.value;
            set => noiseScaleValue.value = value;
        }

        public float coreRadius
        {
            get => coreRadiusValue.value;
            set => coreRadiusValue.value = value;
        }

        public float offsetRange
        {
            get => offsetRangeValue.value;
            set => offsetRangeValue.value = value;
        }

        public float glowWidth
        {
            get => glowWidthValue.value;
            set => glowWidthValue.value = value;
        }

        public int segmentRange
        {
            get => segmentRangeValue.intValue;
            set => segmentRangeValue.intValue = value;
        }

        public bool autoRotation
        {
            get => autoRotationValue.boolValue;
            set => autoRotationValue.boolValue = value;
        }

        public bool autoColor
        {
            get => autoColorValue.boolValue;
            set => autoColorValue.boolValue = value;
        }

        public bool autoLaserInfo
        {
            get => autoLaserInfoValue.boolValue;
            set => autoLaserInfoValue.boolValue = value;
        }

        public bool autoVisible
        {
            get => autoVisibleValue.boolValue;
            set => autoVisibleValue.boolValue = value;
        }

        public bool zTest
        {
            get => zTestValue.boolValue;
            set => zTestValue.boolValue = value;
        }

        public Vector3 rotationMin
        {
            get => rotationMinValues.ToVector3();
            set => rotationMinValues.FromVector3(value);
        }

        public Vector3 rotationMax
        {
            get => rotationMaxValues.ToVector3();
            set => rotationMaxValues.FromVector3(value);
        }

        public void FromStageLaserController(StageLaserController controller)
        {
            var laserInfo = controller.laserInfo;

            position = controller.position;
            eulerAngles = controller.eulerAngles;
            color = controller.color1;
            subColor = controller.color2;
            visible = controller.visible;
            intensity = laserInfo.intensity;
            laserRange = laserInfo.laserRange;
            laserWidth = laserInfo.laserWidth;
            falloffExp = laserInfo.falloffExp;
            noiseStrength = laserInfo.noiseStrength;
            noiseScale = laserInfo.noiseScale;
            coreRadius = laserInfo.coreRadius;
            offsetRange = laserInfo.offsetRange;
            glowWidth = laserInfo.glowWidth;
            segmentRange = laserInfo.segmentRange;
            zTest = laserInfo.zTest;
            autoRotation = controller.autoRotation;
            autoColor = controller.autoColor;
            autoLaserInfo = controller.autoLaserInfo;
            autoVisible = controller.autoVisible;
            rotationMin = controller.rotationMin;
            rotationMax = controller.rotationMax;
        }
    }
}