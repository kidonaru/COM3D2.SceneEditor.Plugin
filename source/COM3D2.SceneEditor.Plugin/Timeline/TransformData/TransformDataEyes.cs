using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataEyes : TransformDataBase
    {
        public enum Index
        {
            Easing = 0,
            Horizon = 1,
            Vertical = 2
        }

        public override TransformType type => TransformType.Eyes;

        public override int valueCount => 3;

        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換と
        // 集約型レイヤーの補間形状キャリアとして残す)
        public override bool hasTangent => true;
        public override ValueData[] tangentValues => values;

        public override ValueData easingValue => values[(int)Index.Easing];

        public TransformDataEyes()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "horizon",
                new CustomValueInfo
                {
                    index = (int)Index.Horizon,
                    name = "水平",
                    defaultValue = 0f,
                }
            },
            {
                "vertical",
                new CustomValueInfo
                {
                    index = (int)Index.Vertical,
                    name = "垂直",
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData horizonValue => values[(int)Index.Horizon];

        public ValueData verticalValue => values[(int)Index.Vertical];

        public float horizon
        {
            get => horizonValue.value;
            set => horizonValue.value = value;
        }

        public float vertical
        {
            get => verticalValue.value;
            set => verticalValue.value = value;
        }
    }
}