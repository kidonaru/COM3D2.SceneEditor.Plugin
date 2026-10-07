using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 顔の影キー 1 件分。項目名は FaceShadowItems の名前 (receiveShadows / castShadows) で、有効フラグを持つ。
    /// 補間せず区間開始時に適用する
    /// </summary>
    public class TransformDataFaceShadow : TransformDataBase
    {
        public enum Index
        {
            IsEnabled = 0,
        }

        public override TransformType type => TransformType.FaceShadow;

        public override int valueCount => 1;

        public TransformDataFaceShadow()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るため、項目ごとのゲームの既定値にそろえる。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            var item = FaceShadowItems.Find(name);
            isEnabled = item != null && item.gameDefault;
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "isEnabled",
                new CustomValueInfo
                {
                    index = (int)Index.IsEnabled,
                    name = "有効",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    // 項目ごとに既定が違う (影を受ける = ON、影を落とす = OFF)。情報は型ごとに 1 つなので受ける側に合わせる
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        private static readonly LaneColorInfo LaneInfo =
            LaneColorInfo.FromBool(LaneColorInfo.DefaultColor, (int)Index.IsEnabled);

        public override LaneColorInfo GetLaneColorInfo()
        {
            return LaneInfo;
        }

        public ValueData isEnabledValue => values[(int)Index.IsEnabled];

        public bool isEnabled
        {
            get => isEnabledValue.boolValue;
            set => isEnabledValue.boolValue = value;
        }
    }
}
