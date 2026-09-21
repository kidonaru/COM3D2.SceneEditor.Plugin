using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataCamera : TransformDataBase
    {
        public override TransformType type => TransformType.Camera;

        public enum Index
        {
            // 距離と FoV は旧「拡縮」の X/Y。旧データを無変換で読むため添字は動かさない
            // (旧 Z にあたる values[9] はダミーで、現在は未使用)
            Distance = 7,
            Fov = 8,
            MaidSlotNo = 10,
            MaidPointType = 11,
            FollowRotation = 12,
        }

        /// <summary>追従設定を持たない旧データの値数</summary>
        private const int LegacyValueCount = 10;

        public override int valueCount => 13;

        public override bool hasPosition => true;
        public override bool hasEulerAngles => true;
        public override bool hasScale => false;
        // Tangent 統一により常に Tangent 補間 (isTangentCamera は XML 互換で残るのみ)
        public override bool hasTangent => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] eulerAnglesValues
        {
            get => new ValueData[] { values[3], values[4], values[5] };
        }

        public override ValueData easingValue => values[6];

        public override ValueData[] tangentValues => values;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "distance", new CustomValueInfo
                {
                    index = (int)Index.Distance,
                    name = "距離",
                    defaultValue = 1f,
                }
            },
            {
                "fov", new CustomValueInfo
                {
                    index = (int)Index.Fov,
                    name = "FoV",
                    defaultValue = 35f,
                }
            },
            {
                "maidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.MaidSlotNo,
                    name = "追従",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
            {
                "maidPointType", new CustomValueInfo
                {
                    index = (int)Index.MaidPointType,
                    name = "追従点",
                    min = 0f,
                    max = (float)MaidPointType.Bip01,
                    step = 1f,
                    // サブカメラと同じ既定値 (股) に合わせる
                    defaultValue = (float)MaidPointType.Crotch,
                    uiType = CustomValueUIType.MaidPoint,
                }
            },
            {
                "followRotation", new CustomValueInfo
                {
                    index = (int)Index.FollowRotation,
                    name = "向き反映",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData distanceValue => values[(int)Index.Distance];
        public ValueData fovValue => values[(int)Index.Fov];
        public ValueData maidSlotNoValue => values[(int)Index.MaidSlotNo];
        public ValueData maidPointTypeValue => values[(int)Index.MaidPointType];
        public ValueData followRotationValue => values[(int)Index.FollowRotation];

        public float distance
        {
            get => distanceValue.value;
            set => distanceValue.value = value;
        }

        public float fov
        {
            get => fovValue.value;
            set => fovValue.value = value;
        }

        public int maidSlotNo
        {
            get => maidSlotNoValue.intValue;
            set => maidSlotNoValue.intValue = value;
        }

        public MaidPointType maidPointType
        {
            get => (MaidPointType)maidPointTypeValue.intValue;
            set => maidPointTypeValue.intValue = (int)value;
        }

        public bool followRotation
        {
            get => followRotationValue.boolValue;
            set => followRotationValue.boolValue = value;
        }

        public TransformDataCamera()
        {
        }

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            // 追従設定を持たない旧データは不足分が 0 で埋まり、スロット 0 のメイドへ追従してしまう。
            // 未追従 (-1) と既定の追従点へ補正する
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                maidSlotNo = -1;
                maidPointType = MaidPointType.Crotch;
                followRotation = false;
            }
        }
    }
}