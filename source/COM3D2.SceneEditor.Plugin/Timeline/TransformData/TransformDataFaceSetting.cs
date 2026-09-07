using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情レイヤーのモーフ以外の設定値。現状は強制上書き (まばたき抑止) の 1 値のみ。
    /// モーフ値 (TransformDataMorph) と型を分けているのは、適用時に
    /// モーフ名として TMorph へ流さないため
    /// </summary>
    public class TransformDataFaceSetting : TransformDataBase
    {
        public enum Index
        {
            ForceOverride = 0
        }

        public override TransformType type => TransformType.FaceSetting;

        public override int valueCount => 1;

        public TransformDataFaceSetting()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るだけなので、ON 既定をここで入れる。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            forceOverride = GetDefaultCustomValue("forceOverride");
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "forceOverride",
                new CustomValueInfo
                {
                    index = (int)Index.ForceOverride,
                    // キーが無い既存データを ON 扱いにするため、既定値は 1 にする
                    name = "強制上書き",
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData forceOverrideValue => values[(int)Index.ForceOverride];

        public float forceOverride
        {
            get => forceOverrideValue.value;
            set => forceOverrideValue.value = value;
        }
    }
}
