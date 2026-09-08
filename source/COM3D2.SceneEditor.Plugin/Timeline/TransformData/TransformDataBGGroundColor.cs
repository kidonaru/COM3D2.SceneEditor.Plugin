
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataBGGroundColor : TransformDataBase
    {
        public static TransformDataBGGroundColor defaultTrans = new TransformDataBGGroundColor();

        public override TransformType type => TransformType.BGGroundColor;

        public override int valueCount => 10;
        public override bool hasPosition => true;
        public override bool hasScale => true;
        public override bool hasVisible => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[3], values[4], values[5] };
        }

        public override ValueData visibleValue => values[9];

        public override Vector3 initialScale => BGGround.DefaultScale;

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgb("色", 6, BGGround.DefaultColor) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public TransformDataBGGroundColor()
        {
        }
    }
}