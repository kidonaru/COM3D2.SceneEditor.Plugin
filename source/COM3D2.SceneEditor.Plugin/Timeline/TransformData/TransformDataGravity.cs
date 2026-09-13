using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 重力キー 1 件分。MaidGravityController のカテゴリ (髪 / スカート) ごとに
    /// 有効フラグとオフセット (-1〜1) を持つ。
    /// 有効フラグは補間せず区間開始時に適用し、オフセットは Tangent 補間する
    /// </summary>
    public class TransformDataGravity : TransformDataBase
    {
        public enum Index
        {
            Enabled = 0,
            X = 1,
            Y = 2,
            Z = 3,
        }

        public override TransformType type => TransformType.Gravity;

        public override int valueCount => 4;

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
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData enabledValue => values[(int)Index.Enabled];

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

        /// <summary>既定値 (無効・オフセット zero) か。適用を省く判定に使う</summary>
        public bool isDefault => !enabled && offset == Vector3.zero;
    }
}
