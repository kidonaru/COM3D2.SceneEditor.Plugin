using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正のキー。基準メイドのスロット番号だけを持ち (-1 は補正なし)、
    /// 区間内は CameraTimelineLayer が 2 人の補正結果を線形補間する
    /// </summary>
    public class TransformDataCameraHeight : TransformDataBase
    {
        public override TransformType type => TransformType.CameraHeight;

        public enum Index
        {
            MaidSlotNo = 0,
        }

        public override int valueCount => 1;

        public override bool hasPosition => false;
        public override bool hasEulerAngles => false;
        public override bool hasScale => false;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap
            = new Dictionary<string, CustomValueInfo>
        {
            {
                "maidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.MaidSlotNo,
                    name = "基準メイド",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData maidSlotNoValue => values[(int)Index.MaidSlotNo];

        public int maidSlotNo
        {
            get => maidSlotNoValue.intValue;
            set => maidSlotNoValue.intValue = value;
        }

        public TransformDataCameraHeight()
        {
        }
    }
}
