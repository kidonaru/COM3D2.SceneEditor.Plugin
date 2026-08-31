using System;
using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タンジェント編集の対象。軸ごとの値種別 (TangentValueType) と、
    /// 軸に属さないカスタム値 (ポストエフェクトの焦点距離など) を同じ土俵で扱う
    /// </summary>
    public struct TangentTarget
    {
        public string name;
        public MTEP.TangentValueType valueType;
        /// <summary>カスタム値のキー。null なら軸ごとの値種別</summary>
        public string customKey;

        public bool isCustom => customKey != null;

        /// <summary>
        /// 選択状態を覚えるための識別子。
        /// 候補は毎フレーム作り直すので添字では覚えられない。
        /// 軸 (axis) とカスタム値でキー空間が衝突しないよう接頭辞で分ける
        /// </summary>
        public string id => isCustom
            ? TangentTargetList.CustomIdPrefix + customKey
            : TangentTargetList.AxisIdPrefix + valueType;
    }

    /// <summary>
    /// 選択キーフレームが実際に持つ編集対象だけを並べたコンボ候補と、その選択状態。
    /// Inspector の補間曲線 (KeyFrameTangentDrawer) と
    /// タイムラインカーブエディタ (TimelineCurveEditor) で共有する。
    /// 先頭には必ず「すべて」が入るので、添字 0 は常に有効。
    /// 候補は excludedValueTypes の違いでインスタンスごとに変わるが、
    /// 選択状態はレイヤー単位で全インスタンス共通 (targetId を参照)
    /// </summary>
    public class TangentTargetList
    {
        public const string AxisIdPrefix = "a:";
        public const string CustomIdPrefix = "c:";

        private static readonly MTEP.TangentData[] EmptyTangents = new MTEP.TangentData[0];
        private static readonly MTEP.ValueData[] EmptyValues = new MTEP.ValueData[0];

        private static readonly string DefaultTargetId
            = AxisIdPrefix + MTEP.TangentValueType.すべて;

        /// <summary>レイヤーごとの選択中の編集対象。
        /// Inspector とカーブエディタで同じ対象を指すようインスタンス外に置く。
        /// レイヤーの実体はロードのたびに作り直されるため、キーは layerName にする</summary>
        private static readonly Dictionary<string, string> TargetIdByLayer
            = new Dictionary<string, string>();

        private static string currentLayerName
        {
            get
            {
                var layer = MTEP.TimelineManager.instance.currentLayer;
                return layer == null ? string.Empty : layer.layerName;
            }
        }

        /// <summary>選択中の編集対象の識別子 (現在のレイヤーのもの)</summary>
        private static string targetId
        {
            get
            {
                string id;
                return TargetIdByLayer.TryGetValue(currentLayerName, out id)
                    ? id
                    : DefaultTargetId;
            }
            set { TargetIdByLayer[currentLayerName] = value; }
        }

        /// <summary>候補から外す軸種別 (タイムライン側は W回転 をカーブに出さないため除く)</summary>
        public readonly HashSet<MTEP.TangentValueType> excludedValueTypes
            = new HashSet<MTEP.TangentValueType>();

        private readonly List<TangentTarget> _targets = new List<TangentTarget>();
        /// <summary>候補へ入れ終えたカスタム値キー (重複判定用。毎フレームの確保を避ける)</summary>
        private readonly HashSet<string> _addedCustomKeys = new HashSet<string>();

        public TangentTargetList()
        {
            _targets.Add(CreateAxisTarget(MTEP.TangentValueType.すべて));
        }

        public List<TangentTarget> items => _targets;

        /// <summary>選択中の編集対象。このインスタンスの候補に無ければ先頭 (すべて) を返す</summary>
        public TangentTarget current
        {
            get
            {
                var index = IndexOf(targetId);
                return index >= 0 ? _targets[index] : _targets[0];
            }
        }

        /// <summary>コンボへ渡す添字。このインスタンスの候補に無ければ 0 (すべて)</summary>
        public int currentIndex => Math.Max(0, IndexOf(targetId));

        public void Select(TangentTarget target)
        {
            targetId = target.id;
        }

        /// <summary>
        /// 候補を、渡されたボーンが実際に持つ対象だけに絞り直す。「すべて」は常に候補に残す。
        /// 選択中の対象が候補から外れても記憶は消さない。
        /// 選択状態はレイヤー単位で Inspector と共有しており、
        /// 一方の除外設定 (excludedValueTypes) や選択ボーンの都合で
        /// もう一方の選択まで巻き添えで消さないため。
        /// 候補に無い間の表示・編集対象は current / currentIndex が「すべて」へ倒す
        /// </summary>
        public void Update(IEnumerable<MTEP.BoneData> bones)
        {
            _targets.Clear();
            _targets.Add(CreateAxisTarget(MTEP.TangentValueType.すべて));

            foreach (MTEP.TangentValueType valueType in
                Enum.GetValues(typeof(MTEP.TangentValueType)))
            {
                if (valueType == MTEP.TangentValueType.すべて
                    || excludedValueTypes.Contains(valueType))
                {
                    continue;
                }
                if (HasValueType(bones, valueType))
                {
                    _targets.Add(CreateAxisTarget(valueType));
                }
            }

            AddCustomValueTargets(bones);
        }

        /// <summary>編集対象に対応するタンジェントを取り出す。対象を持たない transform では空</summary>
        public static MTEP.TangentData[] GetTangents(
            MTEP.ITransformData transform, TangentTarget target, bool isOut)
        {
            if (target.isCustom)
            {
                if (!transform.HasCustomValue(target.customKey))
                {
                    return EmptyTangents;
                }
                var value = transform.GetCustomValue(target.customKey);
                return new[] { isOut ? value.outTangent : value.inTangent };
            }

            return isOut
                ? transform.GetOutTangentDataList(target.valueType)
                : transform.GetInTangentDataList(target.valueType);
        }

        /// <summary>編集対象に対応する値を取り出す。対象を持たない transform では空。
        /// タンジェント列は値列から作られるため、GetTangents と同じ並び・同じ要素数になる
        /// (TransformDataBase.GetOutTangentDataList / GetInTangentDataList)</summary>
        public static MTEP.ValueData[] GetValues(
            MTEP.ITransformData transform, TangentTarget target)
        {
            if (target.isCustom)
            {
                return transform.HasCustomValue(target.customKey)
                    ? new[] { transform.GetCustomValue(target.customKey) }
                    : EmptyValues;
            }

            return transform.GetValueDataList(target.valueType);
        }

        /// <summary>軸ごとの値種別を表す候補を作る</summary>
        private static TangentTarget CreateAxisTarget(MTEP.TangentValueType valueType)
        {
            return new TangentTarget
            {
                name = valueType.ToString(),
                valueType = valueType,
            };
        }

        /// <summary>
        /// カスタム値 (ポストエフェクトの焦点距離など) を候補へ足す。
        /// 選択キーフレームで種類が違うこともあるので、いずれかが持つキーをすべて並べる
        /// </summary>
        private void AddCustomValueTargets(IEnumerable<MTEP.BoneData> bones)
        {
            _addedCustomKeys.Clear();

            foreach (var bone in bones)
            {
                var transform = bone.transform;
                if (transform == null || !transform.hasTangent)
                {
                    continue;
                }

                foreach (var pair in transform.GetCustomValueInfoMap())
                {
                    var customKey = pair.Key;
                    if (!_addedCustomKeys.Add(customKey))
                    {
                        continue;
                    }

                    _targets.Add(new TangentTarget
                    {
                        name = transform.GetCustomValueName(customKey),
                        customKey = customKey,
                    });
                }
            }
        }

        /// <summary>いずれかのボーンが指定種別の値を持つか</summary>
        private static bool HasValueType(
            IEnumerable<MTEP.BoneData> bones, MTEP.TangentValueType valueType)
        {
            foreach (var bone in bones)
            {
                var transform = bone.transform;
                if (transform != null && transform.GetValueDataList(valueType).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private int IndexOf(string targetId)
        {
            return _targets.FindIndex(target => target.id == targetId);
        }
    }
}
