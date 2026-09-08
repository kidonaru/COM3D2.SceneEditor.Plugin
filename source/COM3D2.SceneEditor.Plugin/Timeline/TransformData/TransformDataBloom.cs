using System.Collections.Generic;
using UnityEngine;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ブルーム 1 つ分のキーフレーム値。
    /// 色は 5 つあるが TransformDataBase が面倒を見るのは color / subColor の 2 つまでなので、
    /// しきい値色を color、フレア色 A を subColor に割り当て、
    /// フレア色 B/C/D は RGBA 各チャンネルを CustomValue として持つ
    /// </summary>
    public class TransformDataBloom : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            Visible = 1,

            // しきい値色
            ColorR = 2,
            ColorG = 3,
            ColorB = 4,
            ColorA = 5,

            // フレア色 A
            SubColorR = 6,
            SubColorG = 7,
            SubColorB = 8,
            SubColorA = 9,

            GameEffectDisabled = 10,
            Hdr = 11,
            ScreenBlendMode = 12,
            HighQuality = 13,
            Intensity = 14,
            Threshold = 15,
            BlurIterations = 16,
            BlurSpread = 17,

            SeparationEnabled = 18,
            SeparationCharactersEnabled = 19,
            SeparationBackgroundEnabled = 20,
            SeparationCharacterIntensity = 21,
            SeparationCharacterThreshold = 22,
            SeparationCharacterRadius = 23,

            LensFlareMode = 24,
            LensFlareIntensity = 25,
            LensFlareSaturation = 26,
            LensFlareThreshold = 27,
            FlareRotation = 28,
            HollyStretchWidth = 29,
            HollywoodFlareBlurIterations = 30,

            FlareColorBR = 31,
            FlareColorBG = 32,
            FlareColorBB = 33,
            FlareColorBA = 34,
            FlareColorCR = 35,
            FlareColorCG = 36,
            FlareColorCB = 37,
            FlareColorCA = 38,
            FlareColorDR = 39,
            FlareColorDG = 40,
            FlareColorDB = 41,
            FlareColorDA = 42,
        }

        public static TransformDataBloom defaultTrans = new TransformDataBloom();

        public override TransformType type => TransformType.Bloom;

        public override int valueCount => 43;

        public override bool hasColor => true;
        public override bool hasSubColor => true;
        public override bool hasVisible => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換と
        // 集約型レイヤーの補間形状キャリアとして残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => values;

        public override ValueData[] colorValues
        {
            get => new ValueData[]
            {
                values[(int)Index.ColorR],
                values[(int)Index.ColorG],
                values[(int)Index.ColorB],
                values[(int)Index.ColorA]
            };
        }

        public override ValueData[] subColorValues
        {
            get => new ValueData[]
            {
                values[(int)Index.SubColorR],
                values[(int)Index.SubColorG],
                values[(int)Index.SubColorB],
                values[(int)Index.SubColorA]
            };
        }

        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

        public override Color initialColor => Color.white;
        public override Color initialSubColor => new Color(0.4f, 0.4f, 0.8f, 0.75f);

        public static readonly Color InitialFlareColorB = new Color(0.4f, 0.8f, 0.8f, 0.75f);
        public static readonly Color InitialFlareColorC = new Color(0.8f, 0.4f, 0.8f, 0.75f);
        public static readonly Color InitialFlareColorD = new Color(0.8f, 0.4f, 0f, 0.75f);

        public TransformDataBloom()
        {
        }

        private static CustomValueInfo Toggle(Index index, string name, float defaultValue)
        {
            // bool は 0/1 の 2 値スライダーとして持つ (リムライトの excludeFace 等と同じ作法)
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

        private static CustomValueInfo Channel(Index index, string name, float defaultValue)
        {
            return new CustomValueInfo
            {
                index = (int)index,
                name = name,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = defaultValue,
            };
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            { "gameEffectDisabled", Toggle(Index.GameEffectDisabled, "ゲーム効果無効", 0f) },
            {
                // 0=Auto / 1=On / 2=Off。実体は enum なので丸めて渡す
                "hdr", new CustomValueInfo
                {
                    index = (int)Index.Hdr,
                    name = "HDR",
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
            // 0=Screen / 1=Add の 2 値。CustomValueInfo は min0/max1/step1 を BoolValue と
            // 判定するので、汎用のキーフレーム編集 UI に合わせてトグルとして扱う
            { "screenBlendMode", Toggle(Index.ScreenBlendMode, "加算合成", 0f) },
            { "highQuality", Toggle(Index.HighQuality, "高品質", 1f) },
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "強度",
                    min = 0f,
                    max = 5f,
                    step = 0.01f,
                    defaultValue = 2.1375f,
                }
            },
            {
                "threshold", new CustomValueInfo
                {
                    index = (int)Index.Threshold,
                    name = "しきい値",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.7f,
                }
            },
            {
                "blurIterations", new CustomValueInfo
                {
                    index = (int)Index.BlurIterations,
                    name = "ﾌﾞﾗｰ回数",
                    min = 1f,
                    max = 10f,
                    step = 1f,
                    defaultValue = 3f,
                }
            },
            {
                "blurSpread", new CustomValueInfo
                {
                    index = (int)Index.BlurSpread,
                    name = "ﾌﾞﾗｰ広がり",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 3.48f,
                }
            },

            { "separationEnabled", Toggle(Index.SeparationEnabled, "ｷｬﾗ背景分離", 0f) },
            { "separationCharactersEnabled", Toggle(Index.SeparationCharactersEnabled, "ｷｬﾗ有効", 1f) },
            { "separationBackgroundEnabled", Toggle(Index.SeparationBackgroundEnabled, "背景有効", 1f) },
            {
                "separationCharacterIntensity", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterIntensity,
                    name = "ｷｬﾗ強度",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 2.1375f,
                }
            },
            {
                "separationCharacterThreshold", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterThreshold,
                    name = "ｷｬﾗしきい値",
                    min = 0f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 0.7f,
                }
            },
            {
                "separationCharacterRadius", new CustomValueInfo
                {
                    index = (int)Index.SeparationCharacterRadius,
                    name = "ｷｬﾗ広がり",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 3.48f,
                }
            },

            {
                // 0=Ghosting / 1=Anamorphic / 2=Combined
                "lensFlareMode", new CustomValueInfo
                {
                    index = (int)Index.LensFlareMode,
                    name = "ﾌﾚｱｽﾀｲﾙ",
                    min = 0f,
                    max = 2f,
                    step = 1f,
                    defaultValue = 1f,
                }
            },
            {
                "lensFlareIntensity", new CustomValueInfo
                {
                    index = (int)Index.LensFlareIntensity,
                    name = "ﾌﾚｱ強度",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "lensFlareSaturation", new CustomValueInfo
                {
                    index = (int)Index.LensFlareSaturation,
                    name = "ﾌﾚｱ彩度",
                    min = 0f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0.75f,
                }
            },
            {
                "lensFlareThreshold", new CustomValueInfo
                {
                    index = (int)Index.LensFlareThreshold,
                    name = "ﾌﾚｱしきい値",
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 0.3f,
                }
            },
            {
                "flareRotation", new CustomValueInfo
                {
                    index = (int)Index.FlareRotation,
                    name = "ﾌﾚｱ回転",
                    min = 0f,
                    max = 6.28f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "hollyStretchWidth", new CustomValueInfo
                {
                    index = (int)Index.HollyStretchWidth,
                    name = "伸縮幅",
                    min = 0f,
                    max = 10f,
                    step = 0.01f,
                    defaultValue = 2.5f,
                }
            },
            {
                // 実体側の許容範囲 (BloomSetting.MIN/MAX_HOLLYWOOD_FLARE_BLUR_ITERATIONS) に合わせる
                "hollywoodFlareBlurIterations", new CustomValueInfo
                {
                    index = (int)Index.HollywoodFlareBlurIterations,
                    name = "ﾌﾚｱﾌﾞﾗｰ回数",
                    min = 0f,
                    max = 10f,
                    step = 1f,
                    defaultValue = 2f,
                }
            },

            // フレア色 B/C/D。既定値へリセットできるようチャンネル単位で登録する
            { "flareColorBR", Channel(Index.FlareColorBR, "ﾌﾚｱ色B R", 0.4f) },
            { "flareColorBG", Channel(Index.FlareColorBG, "ﾌﾚｱ色B G", 0.8f) },
            { "flareColorBB", Channel(Index.FlareColorBB, "ﾌﾚｱ色B B", 0.8f) },
            { "flareColorBA", Channel(Index.FlareColorBA, "ﾌﾚｱ色B A", 0.75f) },
            { "flareColorCR", Channel(Index.FlareColorCR, "ﾌﾚｱ色C R", 0.8f) },
            { "flareColorCG", Channel(Index.FlareColorCG, "ﾌﾚｱ色C G", 0.4f) },
            { "flareColorCB", Channel(Index.FlareColorCB, "ﾌﾚｱ色C B", 0.8f) },
            { "flareColorCA", Channel(Index.FlareColorCA, "ﾌﾚｱ色C A", 0.75f) },
            { "flareColorDR", Channel(Index.FlareColorDR, "ﾌﾚｱ色D R", 0.8f) },
            { "flareColorDG", Channel(Index.FlareColorDG, "ﾌﾚｱ色D G", 0.4f) },
            { "flareColorDB", Channel(Index.FlareColorDB, "ﾌﾚｱ色D B", 0f) },
            { "flareColorDA", Channel(Index.FlareColorDA, "ﾌﾚｱ色D A", 0.75f) },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // CustomValueInfo アクセサ
        public CustomValueInfo gameEffectDisabledInfo => GetCustomValueInfo("gameEffectDisabled");
        public CustomValueInfo hdrInfo => GetCustomValueInfo("hdr");
        public CustomValueInfo screenBlendModeInfo => GetCustomValueInfo("screenBlendMode");
        public CustomValueInfo highQualityInfo => GetCustomValueInfo("highQuality");
        public CustomValueInfo intensityInfo => GetCustomValueInfo("intensity");
        public CustomValueInfo thresholdInfo => GetCustomValueInfo("threshold");
        public CustomValueInfo blurIterationsInfo => GetCustomValueInfo("blurIterations");
        public CustomValueInfo blurSpreadInfo => GetCustomValueInfo("blurSpread");
        public CustomValueInfo separationEnabledInfo => GetCustomValueInfo("separationEnabled");
        public CustomValueInfo separationCharactersEnabledInfo => GetCustomValueInfo("separationCharactersEnabled");
        public CustomValueInfo separationBackgroundEnabledInfo => GetCustomValueInfo("separationBackgroundEnabled");
        public CustomValueInfo separationCharacterIntensityInfo => GetCustomValueInfo("separationCharacterIntensity");
        public CustomValueInfo separationCharacterThresholdInfo => GetCustomValueInfo("separationCharacterThreshold");
        public CustomValueInfo separationCharacterRadiusInfo => GetCustomValueInfo("separationCharacterRadius");
        public CustomValueInfo lensFlareModeInfo => GetCustomValueInfo("lensFlareMode");
        public CustomValueInfo lensFlareIntensityInfo => GetCustomValueInfo("lensFlareIntensity");
        public CustomValueInfo lensFlareSaturationInfo => GetCustomValueInfo("lensFlareSaturation");
        public CustomValueInfo lensFlareThresholdInfo => GetCustomValueInfo("lensFlareThreshold");
        public CustomValueInfo flareRotationInfo => GetCustomValueInfo("flareRotation");
        public CustomValueInfo hollyStretchWidthInfo => GetCustomValueInfo("hollyStretchWidth");
        public CustomValueInfo hollywoodFlareBlurIterationsInfo => GetCustomValueInfo("hollywoodFlareBlurIterations");

        // 値アクセサ
        private ValueData[] FlareColorValues(Index startIndex)
        {
            var i = (int)startIndex;
            return new ValueData[] { values[i], values[i + 1], values[i + 2], values[i + 3] };
        }

        public Color flareColorB
        {
            get => FlareColorValues(Index.FlareColorBR).ToColor();
            set => FlareColorValues(Index.FlareColorBR).FromColor(value);
        }

        public Color flareColorC
        {
            get => FlareColorValues(Index.FlareColorCR).ToColor();
            set => FlareColorValues(Index.FlareColorCR).FromColor(value);
        }

        public Color flareColorD
        {
            get => FlareColorValues(Index.FlareColorDR).ToColor();
            set => FlareColorValues(Index.FlareColorDR).FromColor(value);
        }

        public PEP.BloomData bloom
        {
            get => new PEP.BloomData
            {
                enabled = visible,
                gameEffectDisabled = values[(int)Index.GameEffectDisabled].boolValue,
                hdr = values[(int)Index.Hdr].intValue,
                screenBlendMode = values[(int)Index.ScreenBlendMode].intValue,
                highQuality = values[(int)Index.HighQuality].boolValue,
                intensity = values[(int)Index.Intensity].value,
                threshold = values[(int)Index.Threshold].value,
                thresholdColor = color,
                blurIterations = values[(int)Index.BlurIterations].intValue,
                blurSpread = values[(int)Index.BlurSpread].value,

                separationEnabled = values[(int)Index.SeparationEnabled].boolValue,
                separationCharactersEnabled = values[(int)Index.SeparationCharactersEnabled].boolValue,
                separationBackgroundEnabled = values[(int)Index.SeparationBackgroundEnabled].boolValue,
                separationCharacterIntensity = values[(int)Index.SeparationCharacterIntensity].value,
                separationCharacterThreshold = values[(int)Index.SeparationCharacterThreshold].value,
                separationCharacterRadius = values[(int)Index.SeparationCharacterRadius].value,

                lensFlareMode = values[(int)Index.LensFlareMode].intValue,
                lensFlareIntensity = values[(int)Index.LensFlareIntensity].value,
                lensFlareSaturation = values[(int)Index.LensFlareSaturation].value,
                lensFlareThreshold = values[(int)Index.LensFlareThreshold].value,
                flareRotation = values[(int)Index.FlareRotation].value,
                hollyStretchWidth = values[(int)Index.HollyStretchWidth].value,
                hollywoodFlareBlurIterations = values[(int)Index.HollywoodFlareBlurIterations].intValue,
                flareColorA = subColor,
                flareColorB = flareColorB,
                flareColorC = flareColorC,
                flareColorD = flareColorD,
            };
            set
            {
                visible = value.enabled;
                values[(int)Index.GameEffectDisabled].boolValue = value.gameEffectDisabled;
                values[(int)Index.Hdr].intValue = value.hdr;
                values[(int)Index.ScreenBlendMode].intValue = value.screenBlendMode;
                values[(int)Index.HighQuality].boolValue = value.highQuality;
                values[(int)Index.Intensity].value = value.intensity;
                values[(int)Index.Threshold].value = value.threshold;
                color = value.thresholdColor;
                values[(int)Index.BlurIterations].intValue = value.blurIterations;
                values[(int)Index.BlurSpread].value = value.blurSpread;

                values[(int)Index.SeparationEnabled].boolValue = value.separationEnabled;
                values[(int)Index.SeparationCharactersEnabled].boolValue = value.separationCharactersEnabled;
                values[(int)Index.SeparationBackgroundEnabled].boolValue = value.separationBackgroundEnabled;
                values[(int)Index.SeparationCharacterIntensity].value = value.separationCharacterIntensity;
                values[(int)Index.SeparationCharacterThreshold].value = value.separationCharacterThreshold;
                values[(int)Index.SeparationCharacterRadius].value = value.separationCharacterRadius;

                values[(int)Index.LensFlareMode].intValue = value.lensFlareMode;
                values[(int)Index.LensFlareIntensity].value = value.lensFlareIntensity;
                values[(int)Index.LensFlareSaturation].value = value.lensFlareSaturation;
                values[(int)Index.LensFlareThreshold].value = value.lensFlareThreshold;
                values[(int)Index.FlareRotation].value = value.flareRotation;
                values[(int)Index.HollyStretchWidth].value = value.hollyStretchWidth;
                values[(int)Index.HollywoodFlareBlurIterations].intValue = value.hollywoodFlareBlurIterations;
                subColor = value.flareColorA;
                flareColorB = value.flareColorB;
                flareColorC = value.flareColorC;
                flareColorD = value.flareColorD;
            }
        }
    }
}
