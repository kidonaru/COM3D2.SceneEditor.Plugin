
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataBGColor : TransformDataBase
    {
        public override TransformType type => TransformType.BGColor;

        public override int valueCount => 3;

        // 色は Color.Lerp で線形補間するため、タンジェントの対象からは外す。
        // (valuesWithoutColors はこの型では空配列になる)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => valuesWithoutColors;

        private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
            new Dictionary<string, ColorValueInfo>
            {
                { ColorKey.Main, ColorValueInfo.Rgb("色", 0, Color.black) },
            };

        public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;

        public TransformDataBGColor()
        {
        }
    }
}