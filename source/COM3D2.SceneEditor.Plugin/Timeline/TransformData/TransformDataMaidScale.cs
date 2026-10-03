using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// メイドスケールキー 1 件分。腕の骨 1 本の均一倍率 (1 = 元の大きさ) を持ち、Tangent 補間する。
    /// 基底の scale (Vector3) と衝突しないよう、値は multiplier と呼ぶ
    /// </summary>
    public class TransformDataMaidScale : TransformDataBase
    {
        public enum Index
        {
            Multiplier = 0,
        }

        public override TransformType type => TransformType.MaidScale;

        public override int valueCount => 1;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        public TransformDataMaidScale()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るだけなので、元の大きさ (1) を既定にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            multiplier = GetDefaultCustomValue("multiplier");
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "multiplier",
                new CustomValueInfo
                {
                    index = (int)Index.Multiplier,
                    name = "倍率",
                    min = MaidScaleBones.MinScale,
                    max = MaidScaleBones.MaxScale,
                    step = 0.01f,
                    defaultValue = MaidScaleBones.DefaultScale,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData multiplierValue => values[(int)Index.Multiplier];

        public float multiplier
        {
            get => multiplierValue.value;
            set => multiplierValue.value = value;
        }

        /// <summary>元の大きさか。適用を省く判定に使う</summary>
        public bool isDefault => MaidScaleBones.IsDefault(multiplier);
    }
}
