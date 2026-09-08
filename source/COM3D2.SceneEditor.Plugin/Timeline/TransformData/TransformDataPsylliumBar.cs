using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataPsylliumBar : TransformDataBase
    {
        public enum Index
        {
            Color1aR = 0,
            Color1aG = 1,
            Color1aB = 2,
            Color1aA = 3,
            Color1bR = 4,
            Color1bG = 5,
            Color1bB = 6,
            Color1bA = 7,
            Color1cR = 8,
            Color1cG = 9,
            Color1cB = 10,
            Color1cA = 11,
            Color2aR = 12,
            Color2aG = 13,
            Color2aB = 14,
            Color2aA = 15,
            Color2bR = 16,
            Color2bG = 17,
            Color2bB = 18,
            Color2bA = 19,
            Color2cR = 20,
            Color2cG = 21,
            Color2cB = 22,
            Color2cA = 23,
            BaseScale = 24,
            Width = 25,
            Height = 26,
            PositionY = 27,
            Radius = 28,
            TopThreshold = 29,
            CutoffAlpha = 30
        }

        public static TransformDataPsylliumBar defaultTrans = new TransformDataPsylliumBar();
        public static PsylliumBarConfig defaultConfig = new PsylliumBarConfig();

        public override TransformType type => TransformType.PsylliumBar;

        public override int valueCount => 31;

        public TransformDataPsylliumBar()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "baseScale", new CustomValueInfo
                {
                    index = (int)Index.BaseScale,
                    name = "スケール",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = defaultConfig.baseScale,
                }
            },
            {
                "width", new CustomValueInfo
                {
                    index = (int)Index.Width,
                    name = "幅",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = defaultConfig.width,
                }
            },
            {
                "height", new CustomValueInfo
                {
                    index = (int)Index.Height,
                    name = "高さ",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = defaultConfig.height,
                }
            },
            {
                "positionY", new CustomValueInfo
                {
                    index = (int)Index.PositionY,
                    name = "Y",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = defaultConfig.positionY,
                }
            },
            {
                "radius", new CustomValueInfo
                {
                    index = (int)Index.Radius,
                    name = "半径",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = defaultConfig.radius,
                }
            },
            {
                "topThreshold", new CustomValueInfo
                {
                    index = (int)Index.TopThreshold,
                    name = "上部閾値",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = defaultConfig.topThreshold,
                }
            },
            {
                "cutoffAlpha", new CustomValueInfo
                {
                    index = (int)Index.CutoffAlpha,
                    name = "A閾値",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = defaultConfig.cutoffAlpha,
                }
            },
        };

        public const string Color1aKey = "color1a";
        public const string Color1bKey = "color1b";
        public const string Color1cKey = "color1c";
        public const string Color2aKey = "color2a";
        public const string Color2bKey = "color2b";
        public const string Color2cKey = "color2c";

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { Color1aKey, ColorValueInfo.Rgba("中心色1", (int)Index.Color1aR, defaultConfig.color1a) },
                { Color1bKey, ColorValueInfo.Rgba("縁色1", (int)Index.Color1bR, defaultConfig.color1b) },
                { Color1cKey, ColorValueInfo.Rgba("散乱色1", (int)Index.Color1cR, defaultConfig.color1c) },
                { Color2aKey, ColorValueInfo.Rgba("中心色2", (int)Index.Color2aR, defaultConfig.color2a) },
                { Color2bKey, ColorValueInfo.Rgba("縁色2", (int)Index.Color2bR, defaultConfig.color2b) },
                { Color2cKey, ColorValueInfo.Rgba("散乱色2", (int)Index.Color2cR, defaultConfig.color2c) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData baseScaleValue => values[(int)Index.BaseScale];
        public ValueData widthValue => values[(int)Index.Width];
        public ValueData heightValue => values[(int)Index.Height];
        public ValueData positionYValue => values[(int)Index.PositionY];
        public ValueData radiusValue => values[(int)Index.Radius];
        public ValueData topThresholdValue => values[(int)Index.TopThreshold];
        public ValueData cutoffAlphaValue => values[(int)Index.CutoffAlpha];

        public CustomValueInfo baseScaleInfo => CustomValueInfoMap["baseScale"];
        public CustomValueInfo widthInfo => CustomValueInfoMap["width"];
        public CustomValueInfo heightInfo => CustomValueInfoMap["height"];
        public CustomValueInfo positionYInfo => CustomValueInfoMap["positionY"];
        public CustomValueInfo radiusInfo => CustomValueInfoMap["radius"];
        public CustomValueInfo topThresholdInfo => CustomValueInfoMap["topThreshold"];
        public CustomValueInfo cutoffAlphaInfo => CustomValueInfoMap["cutoffAlpha"];

        public Color color1a
        {
            get => GetColorValue(Color1aKey);
            set => SetColorValue(Color1aKey, value);
        }
        public Color color1b
        {
            get => GetColorValue(Color1bKey);
            set => SetColorValue(Color1bKey, value);
        }
        public Color color1c
        {
            get => GetColorValue(Color1cKey);
            set => SetColorValue(Color1cKey, value);
        }
        public Color color2a
        {
            get => GetColorValue(Color2aKey);
            set => SetColorValue(Color2aKey, value);
        }
        public Color color2b
        {
            get => GetColorValue(Color2bKey);
            set => SetColorValue(Color2bKey, value);
        }
        public Color color2c
        {
            get => GetColorValue(Color2cKey);
            set => SetColorValue(Color2cKey, value);
        }
        public float baseScale
        {
            get => baseScaleValue.value;
            set => baseScaleValue.value = value;
        }
        public float width
        {
            get => widthValue.value;
            set => widthValue.value = value;
        }
        public float height
        {
            get => heightValue.value;
            set => heightValue.value = value;
        }
        public float positionY
        {
            get => positionYValue.value;
            set => positionYValue.value = value;
        }
        public float radius
        {
            get => radiusValue.value;
            set => radiusValue.value = value;
        }
        public float topThreshold
        {
            get => topThresholdValue.value;
            set => topThresholdValue.value = value;
        }
        public float cutoffAlpha
        {
            get => cutoffAlphaValue.value;
            set => cutoffAlphaValue.value = value;
        }

        public void FromConfig(PsylliumBarConfig config)
        {
            color1a = config.color1a;
            color1b = config.color1b;
            color1c = config.color1c;
            color2a = config.color2a;
            color2b = config.color2b;
            color2c = config.color2c;
            baseScale = config.baseScale;
            width = config.width;
            height = config.height;
            positionY = config.positionY;
            radius = config.radius;
            topThreshold = config.topThreshold;
            cutoffAlpha = config.cutoffAlpha;
        }

        private PsylliumBarConfig _config = new PsylliumBarConfig();

        public PsylliumBarConfig ToConfig()
        {
            _config.color1a = color1a;
            _config.color1b = color1b;
            _config.color1c = color1c;
            _config.color2a = color2a;
            _config.color2b = color2b;
            _config.color2c = color2c;
            _config.baseScale = baseScale;
            _config.width = width;
            _config.height = height;
            _config.positionY = positionY;
            _config.radius = radius;
            _config.topThreshold = topThreshold;
            _config.cutoffAlpha = cutoffAlpha;
            return _config;
        }
    }
}