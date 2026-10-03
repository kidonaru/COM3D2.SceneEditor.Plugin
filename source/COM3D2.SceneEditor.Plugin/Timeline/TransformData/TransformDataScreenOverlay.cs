using System.Collections.Generic;
using UnityEngine;
using PEP = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// オーバーレイのキーフレーム値。重ねる色は TransformDataBase の color (ColorKey.Main) に持つ。
    /// テクスチャのパスは値配列に載らないため持たない (PostEffects 側 UI の値が残る)
    /// </summary>
    public class TransformDataScreenOverlay : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            Visible = 1,

            ColorR = 2,
            ColorG = 3,
            ColorB = 4,
            ColorA = 5,

            BlendMode = 6,
            Source = 7,
            Intensity = 8,
        }

        // ScreenOverlay.OverlayBlendMode の AlphaBlend (強度が α に掛かるモード)
        public const int BlendModeAlphaBlend = 4;

        // ScreenOverlaySource の値
        public const int SourceTexture = 0;
        public const int SourceColor = 1;

        public static TransformDataScreenOverlay defaultTrans = new TransformDataScreenOverlay();

        public override TransformType type => TransformType.ScreenOverlay;

        public override int valueCount => 9;

        public override bool hasVisible => true;
        // 既定の true だと、キー全登録・リセットで「有効・乗算・テクスチャ未指定 (シェーダー既定の grey)」の
        // キーが生まれて画面が暗くなるため、GTToneMap と同じく無効で始める
        public override bool initialVisible => false;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換のためだけに残す)
        public override bool hasTangent => true;
        // 色は線形補間に統一するためタンジェント編集の対象から外す
        public override ValueData[] tangentValues => valuesWithoutColors;

        public override ValueData visibleValue => values[(int)Index.Visible];
        public override ValueData easingValue => values[(int)Index.Easing];

        public TransformDataScreenOverlay()
        {
        }

        // 範囲と既定値は PostEffects 側 UI (ScreenOverlayController.DrawContent) に合わせる。
        // step 1 の値は Int 扱いで補間されない (CustomValueInfo.type)
        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                // 0=加算 / 1=スクリーン / 2=乗算 / 3=オーバーレイ / 4=アルファ
                "blendMode", new CustomValueInfo
                {
                    index = (int)Index.BlendMode,
                    name = "ﾌﾞﾚﾝﾄﾞ",
                    min = 0f,
                    max = 4f,
                    step = 1f,
                    defaultValue = 2f,
                }
            },
            {
                // 0=テクスチャ / 1=カラー。2 値なのでトグルとして扱う
                "source", new CustomValueInfo
                {
                    index = (int)Index.Source,
                    name = "カラー指定",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = SourceTexture,
                }
            },
            {
                "intensity", new CustomValueInfo
                {
                    index = (int)Index.Intensity,
                    name = "強度",
                    min = 0f,
                    max = 3f,
                    step = 0.01f,
                    defaultValue = 1f,
                }
            },
        };

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色", (int)Index.ColorR, Color.black) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        // ValueData アクセサ
        public ValueData blendModeValue => values[(int)Index.BlendMode];
        public ValueData sourceValue => values[(int)Index.Source];
        public ValueData intensityValue => values[(int)Index.Intensity];

        // CustomValueInfo アクセサ
        public CustomValueInfo blendModeInfo => GetCustomValueInfo("blendMode");
        public CustomValueInfo sourceInfo => GetCustomValueInfo("source");
        public CustomValueInfo intensityInfo => GetCustomValueInfo("intensity");

        // 値アクセサ
        public int blendMode
        {
            get => blendModeValue.intValue;
            set => blendModeValue.intValue = value;
        }

        public int source
        {
            get => sourceValue.intValue;
            set => sourceValue.intValue = value;
        }

        public float intensity
        {
            get => intensityValue.value;
            set => intensityValue.value = value;
        }

        public PEP.ScreenOverlayData screenOverlay
        {
            get => new PEP.ScreenOverlayData
            {
                enabled = visible,
                blendMode = blendMode,
                source = source,
                intensity = intensity,
                color = color,
            };
            set
            {
                visible = value.enabled;
                blendMode = value.blendMode;
                source = value.source;
                intensity = value.intensity;
                color = value.color;
            }
        }
    }
}
