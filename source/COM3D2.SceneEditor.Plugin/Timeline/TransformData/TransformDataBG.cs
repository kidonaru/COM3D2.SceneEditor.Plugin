using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataBG : TransformDataBase
    {
        public enum StrIndex
        {
            BgName = 0,
        }

        public override TransformType type => TransformType.BG;

        public override int valueCount => 9;
        public override int strValueCount => 1;

        public override bool hasPosition => true;
        public override bool hasEulerAngles => true;
        public override bool hasScale => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] eulerAnglesValues
        {
            get => new ValueData[] { values[3], values[4], values[5] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[6], values[7], values[8] };
        }

        private readonly static Dictionary<string, StrValueInfo> StrValueInfoMap = new Dictionary<string, StrValueInfo>
        {
            {
                "bgName",
                new StrValueInfo
                {
                    index = (int)StrIndex.BgName,
                    name = "背景",
                }
            },
        };

        public override Dictionary<string, StrValueInfo> GetStrValueInfoMap()
        {
            return StrValueInfoMap;
        }

        /// <summary>背景アセット名 (BgMgr.GetBGName() の値)。空文字は「背景なし」</summary>
        public string bgName
        {
            get => strValues[(int)StrIndex.BgName];
            set => strValues[(int)StrIndex.BgName] = value;
        }

        public TransformDataBG()
        {
        }
    }
}
