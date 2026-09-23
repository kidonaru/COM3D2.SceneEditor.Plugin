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

        // タイムライン設定で ON/OFF する。旧 XML は OFF で読まれるので線形補間のまま再生される。
        // ロード前 (timeline が null) は false にして XML 直列化判定を安全に通す
        public override bool hasTangent => timeline != null && timeline.isTangentFace;
        public override ValueData[] tangentValues => values;

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
