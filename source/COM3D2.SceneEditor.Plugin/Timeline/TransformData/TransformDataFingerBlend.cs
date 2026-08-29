using System.Collections.Generic;
using UnityEngine;
using SEP = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataFingerBlend : TransformDataBase
    {
        public enum Index
        {
            ValueOpen = 0,
            ValueFist = 1,
            LockEnabled0 = 2,
            LockEnabled1 = 3,
            LockEnabled2 = 4,
            LockEnabled3 = 5,
            LockEnabled4 = 6,
            LockValueOpen0 = 7,
            LockValueOpen1 = 8,
            LockValueOpen2 = 9,
            LockValueOpen3 = 10,
            LockValueOpen4 = 11,
            LockValueFist0 = 12,
            LockValueFist1 = 13,
            LockValueFist2 = 14,
            LockValueFist3 = 15,
            LockValueFist4 = 16
        }

        public override TransformType type => TransformType.FingerBlend;

        public override int valueCount => 17;

        public TransformDataFingerBlend()
        {
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "value_open",
                new CustomValueInfo
                {
                    index = (int)Index.ValueOpen,
                    name = "開き具合",
                    defaultValue = 0,
                }
            },
            {
                "value_fist",
                new CustomValueInfo
                {
                    index = (int)Index.ValueFist,
                    name = "閉じ具合",
                    defaultValue = 0,
                }
            },
            {
                "lock_enabled0",
                new CustomValueInfo
                {
                    index = (int)Index.LockEnabled0,
                    name = "ロック(親)",
                    defaultValue = 0,
                }
            },
            {
                "lock_enabled1",
                new CustomValueInfo
                {
                    index = (int)Index.LockEnabled1,
                    name = "ロック(人)",
                    defaultValue = 0,
                }
            },
            {
                "lock_enabled2",
                new CustomValueInfo
                {
                    index = (int)Index.LockEnabled2,
                    name = "ロック(中)",
                    defaultValue = 0,
                }
            },
            {
                "lock_enabled3",
                new CustomValueInfo
                {
                    index = (int)Index.LockEnabled3,
                    name = "ロック(薬)",
                    defaultValue = 0,
                }
            },
            {
                "lock_enabled4",
                new CustomValueInfo
                {
                    index = (int)Index.LockEnabled4,
                    name = "ロック(子)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_open0",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueOpen0,
                    name = "開き(親)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_open1",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueOpen1,
                    name = "開き(人)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_open2",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueOpen2,
                    name = "開き(中)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_open3",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueOpen3,
                    name = "開き(薬)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_open4",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueOpen4,
                    name = "開き(子)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_fist0",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueFist0,
                    name = "閉じ(親)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_fist1",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueFist1,
                    name = "閉じ(人)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_fist2",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueFist2,
                    name = "閉じ(中)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_fist3",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueFist3,
                    name = "閉じ(薬)",
                    defaultValue = 0,
                }
            },
            {
                "lock_value_fist4",
                new CustomValueInfo
                {
                    index = (int)Index.LockValueFist4,
                    name = "閉じ(子)",
                    defaultValue = 0,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        public ValueData ValueOpenValue => values[(int)Index.ValueOpen];
        public ValueData ValueFistValue => values[(int)Index.ValueFist];
        public ValueData LockEnabled0Value => values[(int)Index.LockEnabled0];
        public ValueData LockEnabled1Value => values[(int)Index.LockEnabled1];
        public ValueData LockEnabled2Value => values[(int)Index.LockEnabled2];
        public ValueData LockEnabled3Value => values[(int)Index.LockEnabled3];
        public ValueData LockEnabled4Value => values[(int)Index.LockEnabled4];

        public ValueData[] LockValue0Values => new ValueData[]
        {
            values[(int)Index.LockValueOpen0],
            values[(int)Index.LockValueFist0]
        };

        public ValueData[] LockValue1Values => new ValueData[]
        {
            values[(int)Index.LockValueOpen1],
            values[(int)Index.LockValueFist1]
        };

        public ValueData[] LockValue2Values => new ValueData[]
        {
            values[(int)Index.LockValueOpen2],
            values[(int)Index.LockValueFist2]
        };

        public ValueData[] LockValue3Values => new ValueData[]
        {
            values[(int)Index.LockValueOpen3],
            values[(int)Index.LockValueFist3]
        };

        public ValueData[] LockValue4Values => new ValueData[]
        {
            values[(int)Index.LockValueOpen4],
            values[(int)Index.LockValueFist4]
        };

        // プロパティアクセサ
        public float ValueOpen
        {
            get => ValueOpenValue.value;
            set => ValueOpenValue.value = value;
        }

        public float ValueFist
        {
            get => ValueFistValue.value;
            set => ValueFistValue.value = value;
        }

        public bool LockEnabled0
        {
            get => LockEnabled0Value.boolValue;
            set => LockEnabled0Value.boolValue = value;
        }

        public bool LockEnabled1
        {
            get => LockEnabled1Value.boolValue;
            set => LockEnabled1Value.boolValue = value;
        }

        public bool LockEnabled2
        {
            get => LockEnabled2Value.boolValue;
            set => LockEnabled2Value.boolValue = value;
        }

        public bool LockEnabled3
        {
            get => LockEnabled3Value.boolValue;
            set => LockEnabled3Value.boolValue = value;
        }

        public bool LockEnabled4
        {
            get => LockEnabled4Value.boolValue;
            set => LockEnabled4Value.boolValue = value;
        }

        public Vector2 LockValue0
        {
            get => LockValue0Values.ToVector2();
            set => LockValue0Values.FromVector2(value);
        }

        public Vector2 LockValue1
        {
            get => LockValue1Values.ToVector2();
            set => LockValue1Values.FromVector2(value);
        }

        public Vector2 LockValue2
        {
            get => LockValue2Values.ToVector2();
            set => LockValue2Values.FromVector2(value);
        }

        public Vector2 LockValue3
        {
            get => LockValue3Values.ToVector2();
            set => LockValue3Values.FromVector2(value);
        }

        public Vector2 LockValue4
        {
            get => LockValue4Values.ToVector2();
            set => LockValue4Values.FromVector2(value);
        }

        private bool GetLockEnabled(int index)
        {
            switch (index)
            {
                case 0: return LockEnabled0;
                case 1: return LockEnabled1;
                case 2: return LockEnabled2;
                case 3: return LockEnabled3;
                case 4: return LockEnabled4;
                default: return false;
            }
        }

        private void SetLockEnabled(int index, bool value)
        {
            switch (index)
            {
                case 0: LockEnabled0 = value; break;
                case 1: LockEnabled1 = value; break;
                case 2: LockEnabled2 = value; break;
                case 3: LockEnabled3 = value; break;
                case 4: LockEnabled4 = value; break;
            }
        }

        private Vector2 GetLockValue(int index)
        {
            switch (index)
            {
                case 0: return LockValue0;
                case 1: return LockValue1;
                case 2: return LockValue2;
                case 3: return LockValue3;
                case 4: return LockValue4;
                default: return Vector2.zero;
            }
        }

        private void SetLockValue(int index, Vector2 value)
        {
            switch (index)
            {
                case 0: LockValue0 = value; break;
                case 1: LockValue1 = value; break;
                case 2: LockValue2 = value; break;
                case 3: LockValue3 = value; break;
                case 4: LockValue4 = value; break;
            }
        }

        /// <summary>
        /// SE の指ブレンドユニットへキー値を適用する。
        /// 指ボーンの書き手はゲームの FingerBlend ではなく SE のコントローラに一本化する
        /// (LockValue の x/y はゲームの lock_value と同じ open/fist)
        /// </summary>
        public void ApplyUnit(SEP.FingerBlendUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            for (var i = 0; i < unit.digitCount; i++)
            {
                var lockValue = GetLockValue(i);
                unit.SetLockState(i, GetLockEnabled(i), lockValue.x, lockValue.y);
            }

            unit.valueOpen = ValueOpen;
            unit.valueFist = ValueFist;
            unit.Apply();
        }

        /// <summary>SE の指ブレンドユニットから現在値を読む (キーフレーム記録用)</summary>
        public void UpdateFromUnit(SEP.FingerBlendUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            ValueOpen = unit.valueOpen;
            ValueFist = unit.valueFist;

            for (var i = 0; i < unit.digitCount; i++)
            {
                SetLockEnabled(i, unit.IsLock(i));
                SetLockValue(i, new Vector2(unit.GetLockOpen(i), unit.GetLockFist(i)));
            }
        }
    }
}