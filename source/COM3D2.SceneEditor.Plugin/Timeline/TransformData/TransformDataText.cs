using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataText : TransformDataBase
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
            ScaleX = 7,
            ScaleY = 8,
            ScaleZ = 9,
            ColorR = 10,
            ColorG = 11,
            ColorB = 12,
            ColorA = 13,
            Easing = 14,
            TextIndex = 15,
            FontSize = 16,
            LineSpacing = 17,
            Alignment = 18,
            SizeDeltaX = 19,
            SizeDeltaY = 20
        }

        public enum StrIndex
        {
            Text = 0,
            Font = 1
        }

        public override TransformType type => TransformType.Text;

        public override int valueCount => 21;
        public override int strValueCount => 2;

        public override bool hasPosition => true;
        public override bool hasRotation => true;
        public override bool hasScale => true;
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換のためだけに残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;

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

        public override ValueData[] scaleValues
        {
            get => new ValueData[] {
                values[(int)Index.ScaleX],
                values[(int)Index.ScaleY],
                values[(int)Index.ScaleZ]
            };
        }

        public override ValueData easingValue => values[(int)Index.Easing];

        public TransformDataText()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "index",
                new CustomValueInfo
                {
                    index = (int)Index.TextIndex,
                    name = "番号",
                    defaultValue = 0f,
                }
            },
            {
                "fontSize",
                new CustomValueInfo
                {
                    index = (int)Index.FontSize,
                    name = "サイズ",
                    defaultValue = 50f,
                }
            },
            {
                "lineSpacing",
                new CustomValueInfo
                {
                    index = (int)Index.LineSpacing,
                    name = "行間",
                    defaultValue = 50f,
                }
            },
            {
                "alignment",
                new CustomValueInfo
                {
                    index = (int)Index.Alignment,
                    name = "整列",
                    defaultValue = 4f,
                }
            },
            {
                "sizeDeltaX",
                new CustomValueInfo
                {
                    index = (int)Index.SizeDeltaX,
                    name = "幅",
                    defaultValue = 1000f,
                }
            },
            {
                "sizeDeltaY",
                new CustomValueInfo
                {
                    index = (int)Index.SizeDeltaY,
                    name = "高さ",
                    defaultValue = 1000f,
                }
            },
        };

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgba("色", (int)Index.ColorR, Color.white) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        private readonly static Dictionary<string, StrValueInfo> StrValueInfoMap = new Dictionary<string, StrValueInfo>
        {
            {
                "text",
                new StrValueInfo
                {
                    index = (int)StrIndex.Text,
                    name = "テキスト",
                    defaultValue = "",
                }
            },
            {
                "font",
                new StrValueInfo
                {
                    index = (int)StrIndex.Font,
                    name = "フォント",
                    defaultValue = "Yu Gothic Bold",
                }
            },
        };

        public override Dictionary<string, StrValueInfo> GetStrValueInfoMap()
        {
            return StrValueInfoMap;
        }

        public ValueData indexValue => values[(int)Index.TextIndex];

        public ValueData fontSizeValue => values[(int)Index.FontSize];

        public ValueData lineSpacingValue => values[(int)Index.LineSpacing];

        public ValueData alignmentValue => values[(int)Index.Alignment];

        public ValueData[] sizeDeltaValues
        {
            get => new ValueData[] {
                values[(int)Index.SizeDeltaX],
                values[(int)Index.SizeDeltaY]
            };
        }

        public int index
        {
            get => indexValue.intValue;
            set => indexValue.intValue = value;
        }

        public string text
        {
            get => strValues[(int)StrIndex.Text];
            set => strValues[(int)StrIndex.Text] = value;
        }

        public string font
        {
            get => strValues[(int)StrIndex.Font];
            set => strValues[(int)StrIndex.Font] = value;
        }

        public int fontSize
        {
            get => fontSizeValue.intValue;
            set => fontSizeValue.intValue = value;
        }

        public int lineSpacing
        {
            get => lineSpacingValue.intValue;
            set => lineSpacingValue.intValue = value;
        }

        public TextAnchor alignment
        {
            get => (TextAnchor) alignmentValue.intValue;
            set => alignmentValue.intValue = (int) value;
        }

        public Vector2 sizeDelta
        {
            get => sizeDeltaValues.ToVector2();
            set => sizeDeltaValues.FromVector2(value);
        }
    }
}
