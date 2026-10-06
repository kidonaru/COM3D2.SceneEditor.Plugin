using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 重力キー 1 件分。MaidGravityController のカテゴリ (髪 / スカート) ごとに
    /// 有効フラグとオフセット (-1〜1)、ローカル（髪は頭、スカートは骨盤の向きに追従）フラグを持つ。
    /// 有効・ローカルは補間せず区間開始時に適用し、オフセットは Tangent 補間する
    /// </summary>
    public class TransformDataGravity : TransformDataBase
    {
        public enum Index
        {
            Enabled = 0,
            X = 1,
            Y = 2,
            Z = 3,
            Local = 4,
        }

        public override TransformType type => TransformType.Gravity;

        public override int valueCount => 5;

        /// <summary>ローカル（index 4）を持たない旧キーの値数</summary>
        public const int LegacyValueCount = 4;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => offsetValues;

        public TransformDataGravity()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "enabled",
                new CustomValueInfo
                {
                    index = (int)Index.Enabled,
                    name = "有効",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
            {
                "x",
                new CustomValueInfo
                {
                    index = (int)Index.X,
                    name = "X",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "y",
                new CustomValueInfo
                {
                    index = (int)Index.Y,
                    name = "Y",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "z",
                new CustomValueInfo
                {
                    index = (int)Index.Z,
                    name = "Z",
                    min = -1f,
                    max = 1f,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "local",
                new CustomValueInfo
                {
                    index = (int)Index.Local,
                    name = "ローカル",
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

        private static readonly LaneColorInfo LaneInfo =
            LaneColorInfo.FromBool(LaneColorInfo.DefaultColor, (int)Index.Enabled);

        public override LaneColorInfo GetLaneColorInfo()
        {
            return LaneInfo;
        }

        public ValueData enabledValue => values[(int)Index.Enabled];

        public ValueData localValue => values[(int)Index.Local];

        public ValueData[] offsetValues
        {
            get => new ValueData[]
            {
                values[(int)Index.X],
                values[(int)Index.Y],
                values[(int)Index.Z],
            };
        }

        public bool enabled
        {
            get => enabledValue.boolValue;
            set => enabledValue.boolValue = value;
        }

        /// <summary>offset をカテゴリの基準ボーン（頭・骨盤）の回転に追従させるか。有効フラグと同じく補間せず区間開始時に適用する</summary>
        public bool local
        {
            get => localValue.boolValue;
            set => localValue.boolValue = value;
        }

        public Vector3 offset
        {
            get => new Vector3(
                values[(int)Index.X].value,
                values[(int)Index.Y].value,
                values[(int)Index.Z].value);
            set
            {
                values[(int)Index.X].value = value.x;
                values[(int)Index.Y].value = value.y;
                values[(int)Index.Z].value = value.z;
            }
        }

        /// <summary>既定値 (無効・ワールド・オフセット zero) か。適用を省く判定に使う</summary>
        public bool isDefault => !enabled && !local && offset == Vector3.zero;

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            // ローカルを持たない旧データ (旧 SE) は、値の不足分の埋め方（0 埋め）に頼らず明示的にワールドとして読む
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                local = false;
            }
        }
    }
}
