using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataMorph : TransformDataBase
    {
        public enum Index
        {
            MorphValue = 0
        }

        public override TransformType type => TransformType.Morph;

        public override int valueCount => 1;

        public TransformDataMorph()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "morphValue",
                new CustomValueInfo
                {
                    index = (int)Index.MorphValue,
                    name = "値",
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData morphValueValue => values[(int)Index.MorphValue];

        public float morphValue
        {
            get => morphValueValue.value;
            set => morphValueValue.value = value;
        }
    }
}
