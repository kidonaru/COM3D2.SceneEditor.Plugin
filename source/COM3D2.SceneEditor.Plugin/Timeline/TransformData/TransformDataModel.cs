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

        public enum StrIndex
        {
            AttachModel = 0,
        }

        /// <summary>アタッチ値を持たない旧データ (MTE 産・version 36 以前) の値数</summary>
        public const int LegacyValueCount = 12;

        /// <summary>アタッチ値を含む現行の値数</summary>
        public const int ValueCount = (int)Index.WorldLerp + 1;

        public override int valueCount => ValueCount;

        public override int strValueCount => 1;

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
                    uiType = CustomValueUIType.AttachTarget,
                }
            },
            {
                "attachPoint", new CustomValueInfo
                {
                    index = (int)Index.AttachPoint,
                    name = "アタッチ部位",
                    // Null (設定なし) はアタッチなしの別表現になり、キーの差分判定がずれるので選ばせない
                    min = (float)AttachPoint.Fix,
                    max = (float)ModelAttachPoints.Max,
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

        private readonly static Dictionary<string, StrValueInfo> StrValueInfoMap = new Dictionary<string, StrValueInfo>
        {
            {
                "attachModel", new StrValueInfo
                {
                    index = (int)StrIndex.AttachModel,
                    name = "アタッチ先モデル",
                    defaultValue = "",
                    // アタッチ先のコンボで一緒に編集するので、文字列欄は出さない
                    hidden = true,
                }
            },
        };

        public override Dictionary<string, StrValueInfo> GetStrValueInfoMap()
        {
            return StrValueInfoMap;
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

        /// <summary>アタッチ先モデルの参照 (ModelAttachTarget の取り決め)。モデル以外へのアタッチでは空</summary>
        public string attachModelName
        {
            get => strValues[(int)StrIndex.AttachModel];
            set => strValues[(int)StrIndex.AttachModel] = value ?? "";
        }

        public bool isAttachedToModel => ModelAttachTarget.IsModelTarget(attachMaidSlotNo, attachModelName);

        /// <summary>メイドへアタッチしているか (旧 SE と同じ判定。モデルへのアタッチは含まない)</summary>
        public static bool IsAttached(AttachPoint point, int maidSlotNo)
        {
            return point != AttachPoint.Null && maidSlotNo >= 0;
        }

        /// <summary>メイドまたはモデルへアタッチしているか</summary>
        public static bool IsAttached(AttachPoint point, int maidSlotNo, string reference)
        {
            return ModelAttachTarget.IsModelTarget(maidSlotNo, reference) || IsAttached(point, maidSlotNo);
        }

        /// <summary>
        /// モデルへアタッチする。部位は使わないので、アタッチなしと同じく既定の Head にそろえる
        /// (値が揺れるとキーの差分や同値区間の判定がずれるため)
        /// </summary>
        public void SetAttachedToModel(string reference)
        {
            attachMaidSlotNo = ModelAttachTarget.ModelSlotNo;
            attachPoint = AttachPoint.Head;
            attachModelName = reference;
        }

        /// <summary>
        /// UI で選んだアタッチ先を入れる。モデル参照ならモデル、0 以上ならそのメイド (部位は今の値を保つ)、それ以外はなし
        /// </summary>
        public void SetAttachTarget(int slotNo, string reference)
        {
            if (ModelAttachTarget.IsModelTarget(slotNo, reference))
            {
                SetAttachedToModel(reference);
                return;
            }
            if (slotNo >= 0)
            {
                attachMaidSlotNo = slotNo;
                attachModelName = "";
                return;
            }
            SetUnattached();
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
            attachModelName = "";
        }

        public TransformDataModel()
        {
        }

        /// <summary>旧データのパス付き .menu 名をファイル名にそろえる。XML 移行の対応付けも同じ規則で引く</summary>
        public static string NormalizeName(string name)
        {
            return name.EndsWith(".menu", System.StringComparison.Ordinal)
                ? Path.GetFileName(name)
                : name;
        }

        public override void FromXml(TransformXml xml)
        {
            base.FromXml(xml);

            name = NormalizeName(name);

            // アタッチ値を持たない旧データは不足分が 0 で埋まり、スロット 0 のメイドへアタッチしてしまう。
            // 未アタッチ (-1) と既定値へ補正する
            if (xml.values != null && xml.values.Length <= LegacyValueCount)
            {
                SetUnattached();
                worldLerp = false;
            }

            // 手編集などで目印とモデル名が食い違ったキーは、取り決めどおりの形へそろえる
            if (attachMaidSlotNo == ModelAttachTarget.ModelSlotNo && string.IsNullOrEmpty(attachModelName))
            {
                SetUnattached();
            }
            else if (attachMaidSlotNo != ModelAttachTarget.ModelSlotNo)
            {
                attachModelName = "";
            }
        }
    }
}
