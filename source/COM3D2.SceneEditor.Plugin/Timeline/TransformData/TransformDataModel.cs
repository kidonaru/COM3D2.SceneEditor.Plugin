using System.Collections.Generic;
using System.IO;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    public class TransformDataModel : TransformDataBase
    {
        public override TransformType type => TransformType.Model;

        public enum Index
        {
            AttachMaidSlotNo = 12,
            AttachPoint = 13,
            WorldLerp = 14,
        }

        /// <summary>アタッチ値を持たない旧データ (MTE 産・version 36 以前) の値数</summary>
        public const int LegacyValueCount = 12;

        public override int valueCount => 15;

        public override bool hasPosition => true;
        public override bool hasRotation => true;
        public override bool hasScale => true;
        public override bool hasVisible => true;
        // Tangent 統一により常に Tangent 補間 (isTangentModel は XML 互換で残るのみ)
        public override bool hasTangent => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[3], values[4], values[5], values[6] };
        }

        public override ValueData[] scaleValues
        {
            get => new ValueData[] { values[7], values[8], values[9] };
        }

        public override ValueData visibleValue => values[11];
        public override ValueData easingValue => values[10];
        public override ValueData[] tangentValues => baseValues;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "attachMaidSlotNo", new CustomValueInfo
                {
                    index = (int)Index.AttachMaidSlotNo,
                    name = "アタッチ先",
                    defaultValue = -1f,
                    uiType = CustomValueUIType.MaidSlot,
                }
            },
            {
                "attachPoint", new CustomValueInfo
                {
                    index = (int)Index.AttachPoint,
                    name = "アタッチ部位",
                    min = 0f,
                    max = (float)AttachPoint.Foot_L,
                    step = 1f,
                    defaultValue = (float)AttachPoint.Head,
                    uiType = CustomValueUIType.AttachPoint,
                }
            },
            {
                "worldLerp", new CustomValueInfo
                {
                    index = (int)Index.WorldLerp,
                    name = "ワールド補間",
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

        public ValueData attachMaidSlotNoValue => values[(int)Index.AttachMaidSlotNo];
        public ValueData attachPointValue => values[(int)Index.AttachPoint];
        public ValueData worldLerpValue => values[(int)Index.WorldLerp];

        public int attachMaidSlotNo
        {
            get => attachMaidSlotNoValue.intValue;
            set => attachMaidSlotNoValue.intValue = value;
        }

        public AttachPoint attachPoint
        {
            get => (AttachPoint)attachPointValue.intValue;
            set => attachPointValue.intValue = (int)value;
        }

        /// <summary>このキーへの区間をワールド座標で補間するか (終点キー側の設定)</summary>
        public bool worldLerp
        {
            get => worldLerpValue.boolValue;
            set => worldLerpValue.boolValue = value;
        }

        public bool isAttached => IsAttached(attachPoint, attachMaidSlotNo);

        public static bool IsAttached(AttachPoint point, int maidSlotNo)
        {
            return point != AttachPoint.Null && maidSlotNo >= 0;
        }

        /// <summary>
        /// キー固有の設定を既存キーから引き継ぐ。
        /// キー登録はシーンの現在状態から値を作り直すため、シーンに実体の無い設定は呼び出し側で残す
        /// </summary>
        public void InheritKeySettings(TransformDataModel existing)
        {
            if (existing == null)
            {
                return;
            }
            worldLerp = existing.worldLerp;
        }

        /// <summary>
        /// アタッチなしを入れる。部位は Null ではなく既定値の Head にそろえる
        /// (アタッチなし同士で部位の値が違うと、キーの差分や同値区間の判定がずれるため)
        /// </summary>
        public void SetUnattached()
        {
            attachMaidSlotNo = -1;
            attachPoint = AttachPoint.Head;
        }

        public TransformDataModel()
        {
        }

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            if (name.EndsWith(".menu", System.StringComparison.Ordinal))
            {
                name = Path.GetFileName(name);
            }

            // アタッチ値を持たない旧データは不足分が 0 で埋まり、スロット 0 のメイドへアタッチしてしまう。
            // 未アタッチ (-1) と既定値へ補正する
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                SetUnattached();
                worldLerp = false;
            }
        }
    }
}
