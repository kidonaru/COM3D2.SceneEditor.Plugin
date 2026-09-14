using System.Collections.Generic;
using System.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public abstract class TransformDataBase : ITransformData
    {
        public string name { get; protected set; }
        public abstract TransformType type { get; }

        public virtual int valueCount => 0;

        private ValueData[] _values = new ValueData[0];
        public ValueData[] values => _values;

        /// <summary>主色 / 副色の固定キー。color / subColor 糖衣が参照する</summary>
        public static class ColorKey
        {
            public const string Main = "color";
            public const string Sub = "subColor";
        }

        // GetValueDataList / GetInTangentDataList / GetOutTangentDataList の結果を
        // 値種別ごとに保持するキャッシュ (添字は (int)TangentValueType)。
        // カーブエディタや Inspector が毎パス何百回も呼ぶため、都度配列を作ると GC 圧になる。
        // _values の要素は Initialize / Clone 以外で差し替わらず、要素内の inTangent / outTangent は
        // InitTangent でだけ差し替わるので、その 3 箇所で捨てる。
        // 返す配列は共有インスタンスなので、呼び出し側は書き換えないこと
        private ValueData[][] _valueDataListCache;
        private TangentData[][] _inTangentListCache;
        private TangentData[][] _outTangentListCache;

        private static readonly int TangentValueTypeCount
            = System.Enum.GetValues(typeof(TangentValueType)).Length;

        private void ClearValueDataListCache()
        {
            _valueDataListCache = null;
            _inTangentListCache = null;
            _outTangentListCache = null;
            _valuesWithoutColors = null;
            _lerpKinds = null;
            // baseValues も values の ValueData を直接参照するキャッシュなので一緒に捨てる。
            // 捨て忘れると Clone 後に複製元の ValueData を指したまま残り、
            // tangentValues => baseValues としている型が複製元へ書き込んでしまう
            _baseValues = null;
        }

        /// <summary>値種別添字のキャッシュから取り出し、未生成なら builder で作って格納する</summary>
        private static T[] GetOrBuildList<T>(
            ref T[][] cache, TangentValueType valueType, System.Func<TangentValueType, T[]> builder)
        {
            if (cache == null)
            {
                cache = new T[TangentValueTypeCount][];
            }
            var index = (int)valueType;
            var list = cache[index];
            if (list == null)
            {
                list = builder(valueType);
                cache[index] = list;
            }
            return list;
        }

        public virtual int strValueCount => 0;

        private string[] _strValues = new string[0];
        public string[] strValues => _strValues;

        public Vector3 position
        {
            get => positionValues.ToVector3();
            set => positionValues.FromVector3(value);
        }

        public Vector3 subPosition
        {
            get => subPositionValues.ToVector3();
            set => subPositionValues.FromVector3(value);
        }

        public Quaternion rotation
        {
            get => rotationValues.ToQuaternion();
            set => rotationValues.FromQuaternion(value);
        }

        public Quaternion subRotation
        {
            get => subRotationValues.ToQuaternion();
            set => subRotationValues.FromQuaternion(value);
        }

        public Vector3 eulerAngles
        {
            get
            {
                if (hasEulerAngles)
                {
                    return eulerAnglesValues.ToVector3();
                }
                else if (hasRotation)
                {
                    return rotation.eulerAngles;
                }
                return Vector3.zero;
            }
            set
            {
                if (hasEulerAngles)
                {
                    eulerAnglesValues.FromVector3(value);
                }
                else if (hasRotation)
                {
                    rotation = Quaternion.Euler(value);
                }
            }
        }

        public Vector3 subEulerAngles
        {
            get
            {
                if (hasSubEulerAngles)
                {
                    return subEulerAnglesValues.ToVector3();
                }
                else if (hasSubRotation)
                {
                    return subRotation.eulerAngles;
                }
                return Vector3.zero;
            }
            set
            {
                if (hasSubEulerAngles)
                {
                    subEulerAnglesValues.FromVector3(value);
                }
                else if (hasSubRotation)
                {
                    subRotation = Quaternion.Euler(value);
                }
            }
        }

        public Vector3 normalizedEulerAngles
        {
            get => GetNormalizedEulerAngles(eulerAngles);
        }

        public Vector3 normalizedSubEulerAngles
        {
            get => GetNormalizedEulerAngles(subEulerAngles);
        }

        public Vector3 scale
        {
            get => scaleValues.ToVector3();
            set => scaleValues.FromVector3(value);
        }

        public Color color
        {
            get => GetColorValue(ColorKey.Main);
            set => SetColorValue(ColorKey.Main, value);
        }

        public Color subColor
        {
            get => GetColorValue(ColorKey.Sub);
            set => SetColorValue(ColorKey.Sub, value);
        }

        public bool visible
        {
            get => visibleValue.boolValue;
            set => visibleValue.boolValue = value;
        }

        public int easing
        {
            get => easingValue.intValue;
            set => easingValue.intValue = value;
        }

        public virtual bool hasPosition => false;
        public virtual bool hasSubPosition => false;
        public virtual bool hasRotation => false;
        public virtual bool hasSubRotation => false;
        public virtual bool hasEulerAngles => false;
        public virtual bool hasSubEulerAngles => false;
        public virtual bool hasScale => false;
        public virtual bool hasVisible => false;
        /// <summary>easing 値のスロットを values 内に持つ型か (旧 easing 型の判定に使う)</summary>
        private bool? _hasEasingChannel = null;
        public bool hasEasingChannel
        {
            get
            {
                // 型ごとに不変。タンジェント統一の判定から繰り返し呼ばれるためキャッシュする
                if (_hasEasingChannel == null)
                {
                    var slot = easingValue;
                    _hasEasingChannel = false;
                    foreach (var value in values)
                    {
                        if (ReferenceEquals(value, slot))
                        {
                            _hasEasingChannel = true;
                            break;
                        }
                    }
                }
                return _hasEasingChannel.Value;
            }
        }
        public virtual bool hasTangent => false;

        public virtual bool isHidden => false;
        public virtual bool isGlobal => false;

        public virtual ValueData[] positionValues => new ValueData[0];
        public virtual ValueData[] subPositionValues => new ValueData[0];
        public virtual ValueData[] rotationValues => new ValueData[0];
        public virtual ValueData[] subRotationValues => new ValueData[0];
        public virtual ValueData[] eulerAnglesValues => new ValueData[0];
        public virtual ValueData[] subEulerAnglesValues => new ValueData[0];
        public virtual ValueData[] scaleValues => new ValueData[0];
        public virtual ValueData visibleValue => new ValueData();
        public virtual ValueData easingValue => new ValueData();

        public virtual ValueData[] tangentValues => new ValueData[0];

        private ValueData[] _valuesWithoutColors = null;

        /// <summary>
        /// values から色マップの全成分を除いた配列。色は線形補間に統一するため
        /// タンジェント編集の対象にしない型が tangentValues として返す。
        /// _values の差し替え時 (Initialize / Clone) に ClearValueDataListCache で捨てる
        /// </summary>
        protected ValueData[] valuesWithoutColors
        {
            get
            {
                if (_valuesWithoutColors != null)
                {
                    return _valuesWithoutColors;
                }

                var colorIndices = new HashSet<int>();
                foreach (var info in GetColorValueInfoMap().Values)
                {
                    colorIndices.Add(info.indexR);
                    colorIndices.Add(info.indexG);
                    colorIndices.Add(info.indexB);
                    if (info.hasAlpha)
                    {
                        colorIndices.Add(info.indexA);
                    }
                }

                var list = new List<ValueData>(values.Length);
                for (var i = 0; i < values.Length; i++)
                {
                    if (!colorIndices.Contains(i))
                    {
                        list.Add(values[i]);
                    }
                }
                _valuesWithoutColors = list.ToArray();
                return _valuesWithoutColors;
            }
        }

        private ValueData[] _baseValues = null;

        public ValueData[] baseValues
        {
            get
            {
                if (_baseValues != null)
                {
                    return _baseValues;
                }

                var length = 0;
                if (hasPosition) length += 3;
                if (hasRotation) length += 4;
                if (hasEulerAngles) length += 3;
                if (hasScale) length += 3;
                _baseValues = new ValueData[length];

                int index = 0;
                if (hasPosition)
                {
                    var positionValues = this.positionValues;
                    _baseValues[index++] = positionValues[0];
                    _baseValues[index++] = positionValues[1];
                    _baseValues[index++] = positionValues[2];
                }
                if (hasRotation)
                {
                    var rotationValues = this.rotationValues;
                    _baseValues[index++] = rotationValues[0];
                    _baseValues[index++] = rotationValues[1];
                    _baseValues[index++] = rotationValues[2];
                    _baseValues[index++] = rotationValues[3];
                }
                if (hasEulerAngles)
                {
                    var eulerAnglesValues = this.eulerAnglesValues;
                    _baseValues[index++] = eulerAnglesValues[0];
                    _baseValues[index++] = eulerAnglesValues[1];
                    _baseValues[index++] = eulerAnglesValues[2];
                }
                if (hasScale)
                {
                    var scaleValues = this.scaleValues;
                    _baseValues[index++] = scaleValues[0];
                    _baseValues[index++] = scaleValues[1];
                    _baseValues[index++] = scaleValues[2];
                }

                return _baseValues;
            }
        }

        public virtual Vector3 initialPosition => Vector3.zero;

        public virtual Vector3 initialSubPosition => Vector3.zero;

        public virtual Quaternion initialRotation => Quaternion.identity;

        public virtual Quaternion initialSubRotation => Quaternion.identity;

        public virtual Vector3 initialEulerAngles => Vector3.zero;

        public virtual Vector3 initialSubEulerAngles => Vector3.zero;

        public virtual Vector3 initialScale => Vector3.one;

        public virtual bool initialVisible => true;

        public virtual SingleFrameType singleFrameType => timeline.singleFrameType;

        public ValueData this[string name]
        {
            get => GetCustomValue(name);
        }

        public static readonly string[] PositionNames = new string[] { "X", "Y", "Z" };
        public static readonly string[] RotationNames = new string[] { "RX", "RY", "RZ", "RW" };
        public static readonly string[] ScaleNames = new string[] { "SX", "SY", "SZ" };

        protected static Config config => ConfigManager.instance.config;

        protected static TimelineManager timelineManager => TimelineManager.instance;

        protected static TimelineData timeline => timelineManager.timeline;

        protected static MaidManager maidManager => MaidManager.instance;

        protected static MaidCache maidCache => maidManager.maidCache;

        protected static StudioModelManager modelManager => StudioModelManager.instance;

        public virtual void Initialize(string name)
        {
            this.name = name;

            var length = valueCount;
            if (_values.Length != length)
            {
                _values = new ValueData[length];
                ClearValueDataListCache();

                var tangentPair = config.defaultTangentPair;

                for (int i = 0; i < length; i++)
                {
                    _values[i] = new ValueData
                    {
                        inTangent = new TangentData
                        {
                            normalizedValue = tangentPair.inTangent,
                            isSmooth = tangentPair.isSmooth,
                        },
                        outTangent = new TangentData
                        {
                            normalizedValue = tangentPair.outTangent,
                            isSmooth = tangentPair.isSmooth,
                        },
                    };
                }
            }

            if (_strValues.Length != strValueCount)
            {
                _strValues = new string[strValueCount];
                for (int i = 0; i < strValueCount; i++)
                {
                    _strValues[i] = string.Empty;
                }
            }
        }

        /// <summary>
        /// 最短の補間経路に修正
        /// </summary>
        /// <param name="prevBone"></param>
        public void FixRotation(ITransformData _prevTrans)
        {
            if (!hasRotation)
            {
                return;
            }

            var prevTrans = _prevTrans as TransformDataBase;
            if (prevTrans == null || prevTrans == this)
            {
                return;
            }

            var prevRot = prevTrans.rotation;
            var rot = this.rotation;
            var dot = Quaternion.Dot(prevRot, rot);

            if (dot < 0.0f)
            {
                this.rotation = new Quaternion(-rot.x, -rot.y, -rot.z, -rot.w);
            }
        }

        public static Vector3 GetFixedEulerAngles(Vector3 angles, Vector3 prevAngles)
        {
            return AngleUtils.GetFixedAngles(angles, prevAngles);
        }

        public static Vector3 GetNormalizedEulerAngles(Vector3 angles)
        {
            return AngleUtils.NormalizeAngles(angles);
        }

        public void FixEulerAngles(ITransformData _prevTrans)
        {
            if (!hasEulerAngles)
            {
                return;
            }

            var prevTrans = _prevTrans as TransformDataBase;
            if (prevTrans == null || prevTrans == this)
            {
                return;
            }

            var prevEuler = prevTrans.eulerAngles;
            var euler = this.eulerAngles;
            this.eulerAngles = GetFixedEulerAngles(euler, prevEuler);
        }

        public void UpdateTangent(
            ITransformData prevTransform,
            ITransformData nextTransform,
            float prevTime,
            float currentTime,
            float nextTime)
        {
            if (!hasTangent)
            {
                return;
            }

            if (prevTransform == null || nextTransform == null)
            {
                MTEUtils.LogError("UpdateTangent：前後のTransformが見つかりません。");
                return;
            }

            var prevValues = prevTransform.tangentValues;
            var currentValues = this.tangentValues;
            var nextValues = nextTransform.tangentValues;
            float dt0 = currentTime - prevTime;
            float dt1 = nextTime - currentTime;
            float dt = dt0 + dt1;

            if (dt0 <= 0f || dt1 <= 0f)
            {
                MTEUtils.LogError("UpdateTangent：フレーム時間が不正です。");
                return;
            }

            float dt0_inv = 1f / dt0;
            float dt1_inv = 1f / dt1;
            float dt_inv = 1f / dt;

            for (int i = 0; i < currentValues.Length; i++)
            {
                var inTangent = currentValues[i].inTangent;
                var outTangent = currentValues[i].outTangent;

                float x0 = prevValues[i].value;
                float x1 = currentValues[i].value;
                float x2 = nextValues[i].value;
                float dx0 = x1 - x0;
                float dx1 = x2 - x1;

                if (dx0 == 0f && dx1 == 0f)
                {
                    // do nothing
                }
                else if (dx0 == 0f)
                {
                    dx0 = Mathf.Sign(dx1) * 0.01f;
                }
                else if (dx1 == 0f)
                {
                    dx1 = Mathf.Sign(dx0) * 0.01f;
                }

                float v0 = dx0 * dt0_inv;
                float v1 = dx1 * dt1_inv;

                // 値が変化しないチャンネル (v0 / v1 が 0) は自動補間の基準勾配を作れない。
                // ここで normalizedValue を 0 で潰すと、XML 互換のために残している easing
                // スロットの値まで失われるため、既存値を維持する
                if ((inTangent.isSmooth || outTangent.isSmooth) && v0 != 0f && v1 != 0f)
                {
                    var tan = (x2 - x0) * dt_inv;

                    if (inTangent.isSmooth)
                    {
                        inTangent.normalizedValue = tan / v0;
                    }
                    if (outTangent.isSmooth)
                    {
                        outTangent.normalizedValue = tan / v1;
                    }
                }

                inTangent.UpdateValue(v0);
                outTangent.UpdateValue(v1);
            }
        }

        public void FromTransformData(ITransformData transform)
        {
            for (int i = 0; i < valueCount; i++)
            {
                values[i].FromValue(transform.values[i]);
            }

            for (int i = 0; i < strValueCount; i++)
            {
                strValues[i] = transform.strValues[i];
            }
        }

        /// <summary>
        /// 値と文字列値がすべて一致するか。区間の始点・終点が同値なら補間結果が定数になるため、
        /// 再生時に補間計算をスキップしてよいかの判定に使う。
        /// タンジェントは比較しない (ValueData.Equals は数値本体だけを見る)
        /// </summary>
        public bool IsSameValues(ITransformData other)
        {
            if (other == null || other.type != type)
            {
                return false;
            }

            var otherValues = other.values;
            if (otherValues.Length != _values.Length)
            {
                return false;
            }

            for (int i = 0; i < _values.Length; i++)
            {
                if (!_values[i].Equals(otherValues[i]))
                {
                    return false;
                }
            }

            var otherStrValues = other.strValues;
            if (otherStrValues.Length != _strValues.Length)
            {
                return false;
            }

            for (int i = 0; i < _strValues.Length; i++)
            {
                if (_strValues[i] != otherStrValues[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>値 1 個の補間種別。型ごとに不変なので初回に作って使い回す</summary>
        private enum LerpKind
        {
            /// <summary>区間開始値をコピー (Bool / Int / easing / 表示フラグなど)</summary>
            Hold,
            /// <summary>線形補間 (色成分)</summary>
            Linear,
            /// <summary>その値自身の out / in タンジェントでエルミート補間</summary>
            Tangent,
        }

        private LerpKind[] _lerpKinds = null;

        private LerpKind[] lerpKinds
        {
            get
            {
                if (_lerpKinds != null)
                {
                    return _lerpKinds;
                }

                var kinds = new LerpKind[values.Length];

                foreach (var info in GetColorValueInfoMap().Values)
                {
                    kinds[info.indexR] = LerpKind.Linear;
                    kinds[info.indexG] = LerpKind.Linear;
                    kinds[info.indexB] = LerpKind.Linear;
                    if (info.hasAlpha)
                    {
                        kinds[info.indexA] = LerpKind.Linear;
                    }
                }

                // タンジェント補間の対象は「タンジェントを保存している値」かつ「連続値」だけ。
                // Bool / Int は中間値に意味が無いので区間開始値のまま保つ。
                // ValueData は Equals を数値本体だけで上書きしているため Array.IndexOf は使えない
                // (既定値 0 同士が一致して常に添字 0 を返す)。必ず参照で突き合わせること
                var tangentIndices = new HashSet<int>();
                var tangents = tangentValues;
                var bases = baseValues;
                for (var i = 0; i < values.Length; i++)
                {
                    var isTangent = false;
                    foreach (var tangentValue in tangents)
                    {
                        if (ReferenceEquals(values[i], tangentValue))
                        {
                            isTangent = true;
                            tangentIndices.Add(i);
                            break;
                        }
                    }
                    if (!isTangent || kinds[i] != LerpKind.Hold)
                    {
                        continue;
                    }

                    // 位置・回転・拡縮は CustomValueInfoMap に載らないが、いずれも連続値なので
                    // タンジェント補間の対象にする。ここで拾わないと Hold に落ちて補間が効かない
                    // (リムライトの光源方向が MTE からの移植時にこれで無補間になっていた)。
                    // baseValues は sub 系 (subPosition / subEulerAngles) を含まない。
                    // sub 系を持つ型を LerpFrom 経路へ乗せる際は要確認
                    foreach (var baseValue in bases)
                    {
                        if (ReferenceEquals(values[i], baseValue))
                        {
                            kinds[i] = LerpKind.Tangent;
                            break;
                        }
                    }
                }

                foreach (var info in GetCustomValueInfoMap().Values)
                {
                    if (info.index < 0 || info.index >= kinds.Length)
                    {
                        continue;
                    }
                    if (kinds[info.index] != LerpKind.Hold || !tangentIndices.Contains(info.index))
                    {
                        continue;
                    }
                    if (info.type == CustomValueType.FloatValue || info.type == CustomValueType.FloatSlider)
                    {
                        kinds[info.index] = LerpKind.Tangent;
                    }
                }

                _lerpKinds = kinds;
                return _lerpKinds;
            }
        }

        /// <summary>
        /// start〜end の区間を時刻 t (秒。t0〜t1 の範囲) で補間した値を自身へ書き込む。
        /// 集約型レイヤー (ポストエフェクト・マテリアル) の再生用で、毎フレーム同じ
        /// インスタンスへ書き込む前提。色は線形、数値はその値自身のタンジェント、
        /// Bool / Int と文字列は区間開始値になる
        /// </summary>
        public void LerpFrom(
            ITransformData start,
            ITransformData end,
            float t0,
            float t1,
            float t)
        {
            var startValues = start.values;
            var endValues = end.values;
            var kinds = lerpKinds;

            var count = Mathf.Min(values.Length, Mathf.Min(startValues.Length, endValues.Length));
            for (var i = 0; i < count; i++)
            {
                switch (kinds[i])
                {
                    case LerpKind.Linear:
                        values[i].value = Mathf.Lerp(startValues[i].value, endValues[i].value, t);
                        break;
                    case LerpKind.Tangent:
                        values[i].value = PluginUtils.HermiteValue(
                            t0, t1, startValues[i], endValues[i], t);
                        break;
                    default:
                        // float プロパティを介すと double が丸まる。開始値はそのまま保つ
                        values[i]._value = startValues[i]._value;
                        break;
                }
            }

            var strCount = Mathf.Min(strValues.Length, start.strValues.Length);
            for (var i = 0; i < strCount; i++)
            {
                strValues[i] = start.strValues[i];
            }
        }

        public void InitTangent()
        {
            if (hasTangent)
            {
                var tangentPair = config.defaultTangentPair;

                foreach (var value in _values)
                {
                    value.inTangent = new TangentData
                    {
                        normalizedValue = tangentPair.inTangent,
                        isSmooth = tangentPair.isSmooth,
                    };
                    value.outTangent = new TangentData
                    {
                        normalizedValue = tangentPair.outTangent,
                        isSmooth = tangentPair.isSmooth,
                    };
                }

                // タンジェント参照を差し替えたので、古い TangentData を指すキャッシュを捨てる
                ClearValueDataListCache();
            }

        }

        protected float[] _valuesForXml
        {
            get
            {
                var result = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    result[i] = values[i].value;
                }
                return result;
            }
            set
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i < value.Length)
                    {
                        values[i].value = value[i];
                    }
                    else
                    {
                        values[i].value = 0f;
                    }
                }
            }
        }

        protected float[] _normalizedInTangents
        {
            get
            {
                if (!ShouldSerializeInTangents())
                {
                    return null;
                }
                var result = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    result[i] = values[i].inTangent.normalizedValue;
                }
                return result;
            }
            set
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (value != null && i < value.Length)
                    {
                        values[i].inTangent.normalizedValue = value[i];
                    }
                    else
                    {
                        values[i].inTangent.normalizedValue = 0f;
                    }
                }
            }
        }

        public bool ShouldSerializeInTangents()
        {
            if (!hasTangent)
            {
                return false;
            }
            return values.Any(value => value.inTangent.shouldSerialize);
        }

        protected float[] _normalizedOutTangents
        {
            get
            {
                if (!ShouldSerializeOutTangents())
                {
                    return null;
                }
                var result = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    result[i] = values[i].outTangent.normalizedValue;
                }
                return result;
            }
            set
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (value != null && i < value.Length)
                    {
                        values[i].outTangent.normalizedValue = value[i];
                    }
                    else
                    {
                        values[i].outTangent.normalizedValue = 0f;
                    }
                }
            }
        }

        public bool ShouldSerializeOutTangents()
        {
            if (!hasTangent)
            {
                return false;
            }
            return values.Any(value => value.outTangent.shouldSerialize);
        }

        protected long _inSmoothBit
        {
            get
            {
                if (!hasTangent)
                {
                    return 0;
                }

                long result = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    long value = values[i].inTangent.isSmooth ? 1 : 0;
                    result |= value << i;
                }
                return result;
            }
            set
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].inTangent.isSmooth = (value & ((long) 1 << i)) != 0;
                }
            }
        }

        protected long _outSmoothBit
        {
            get
            {
                if (!hasTangent)
                {
                    return 0;
                }

                long result = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    long value = values[i].outTangent.isSmooth ? 1 : 0;
                    result |= value << i;
                }
                return result;
            }
            set
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].outTangent.isSmooth = (value & ((long) 1 << i)) != 0;
                }
            }
        }

        private string[] _strValuesForXml
        {
            get
            {
                if (strValues.Length == 0)
                {
                    return null;
                }
                return strValues;
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    if (i >= strValues.Length)
                    {
                        break;
                    }
                    strValues[i] = value[i];
                }
            }
        }

        public virtual void FromXml(TransformXml xml)
        {
            name = xml.name;
            _valuesForXml = xml.values;
            _normalizedInTangents = xml.inTangents;
            _normalizedOutTangents = xml.outTangents;
            _inSmoothBit = xml.inSmoothBit;
            _outSmoothBit = xml.outSmoothBit;
            _strValuesForXml = xml.strValues;
        }

        public virtual TransformXml ToXml()
        {
            var xml = new TransformXml
            {
                name = name,
                type = type,
                values = _valuesForXml,
                inTangents = _normalizedInTangents,
                outTangents = _normalizedOutTangents,
                inSmoothBit = _inSmoothBit,
                outSmoothBit = _outSmoothBit,
                strValues = _strValuesForXml,
            };
            return xml;
        }

        /// <summary>カスタム値を持たない型が毎フレームの UI 描画で空マップを作り直さないよう使い回す</summary>
        private static readonly Dictionary<string, CustomValueInfo> EmptyCustomValueInfoMap
            = new Dictionary<string, CustomValueInfo>();

        public virtual Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return EmptyCustomValueInfoMap;
        }

        public CustomValueInfo GetCustomValueInfo(string customKey)
        {
            CustomValueInfo info;
            if (GetCustomValueInfoMap().TryGetValue(customKey, out info))
            {
                return info;
            }

            MTEUtils.LogError("CustomValueが見つかりません customKey={0}", customKey);
            return null;
        }

        public ValueData GetCustomValue(string customKey)
        {
            var info = GetCustomValueInfo(customKey);
            if (info != null)
            {
                return values[info.index];
            }
            return new ValueData();
        }

        public string GetCustomValueName(string customKey)
        {
            var info = GetCustomValueInfo(customKey);
            if (info != null)
            {
                return info.name;
            }
            return customKey;
        }

        public bool HasCustomValue(string customKey)
        {
            return GetCustomValueInfoMap().ContainsKey(customKey);
        }

        public float GetDefaultCustomValue(string customKey)
        {
            var info = GetCustomValueInfo(customKey);
            if (info != null)
            {
                return info.defaultValue;
            }
            return 0f;
        }

        /// <summary>色を持たない型が毎フレームの UI 描画で空マップを作り直さないよう使い回す</summary>
        private static readonly Dictionary<string, ColorValueInfo> EmptyColorValueInfoMap
            = new Dictionary<string, ColorValueInfo>();

        /// <summary>
        /// 色マップ。CustomValueInfoMap と同じく型ごとに不変なので、
        /// override する型は static readonly のマップを返すこと
        /// (Reset / UI 描画から毎フレーム呼ばれるため都度生成するとGC圧になる)
        /// </summary>
        public virtual Dictionary<string, ColorValueInfo> GetColorValueInfoMap()
        {
            return EmptyColorValueInfoMap;
        }

        public ColorValueInfo GetColorValueInfo(string colorKey)
        {
            ColorValueInfo info;
            if (GetColorValueInfoMap().TryGetValue(colorKey, out info))
            {
                return info;
            }

            MTEUtils.LogError("ColorValueが見つかりません colorKey={0}", colorKey);
            return null;
        }

        /// <summary>RGB のみの色はアルファ 1 で返す</summary>
        public Color GetColorValue(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            if (info == null)
            {
                return Color.white;
            }
            return new Color(
                values[info.indexR].value,
                values[info.indexG].value,
                values[info.indexB].value,
                info.hasAlpha ? values[info.indexA].value : 1f);
        }

        /// <summary>RGB のみの色はアルファを捨てる</summary>
        public void SetColorValue(string colorKey, Color color)
        {
            var info = GetColorValueInfo(colorKey);
            if (info == null)
            {
                return;
            }
            values[info.indexR].value = color.r;
            values[info.indexG].value = color.g;
            values[info.indexB].value = color.b;
            if (info.hasAlpha)
            {
                values[info.indexA].value = color.a;
            }
        }

        public Color GetDefaultColorValue(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            return info != null ? info.defaultValue : Color.white;
        }

        public bool HasColorValue(string colorKey)
        {
            return GetColorValueInfoMap().ContainsKey(colorKey);
        }

        public string GetColorValueName(string colorKey)
        {
            var info = GetColorValueInfo(colorKey);
            return info != null ? info.name : colorKey;
        }

        public virtual Dictionary<string, StrValueInfo> GetStrValueInfoMap()
        {
            return new Dictionary<string, StrValueInfo>();
        }

        public StrValueInfo GetStrValueInfo(string keyName)
        {
            StrValueInfo info;
            if (GetStrValueInfoMap().TryGetValue(keyName, out info))
            {
                return info;
            }

            MTEUtils.LogError("StrValueが見つかりません keyName={0}", keyName);
            return null;
        }

        public string GetStrValue(string keyName)
        {
            var info = GetStrValueInfo(keyName);
            if (info != null)
            {
                return strValues[info.index];
            }
            return string.Empty;
        }

        public string GetStrValueName(string keyName)
        {
            var info = GetStrValueInfo(keyName);
            if (info != null)
            {
                return info.name;
            }
            return keyName;
        }

        public void SetStrValue(string keyName, string value)
        {
            var info = GetStrValueInfo(keyName);
            if (info != null)
            {
                strValues[info.index] = value;
            }
        }

        public bool HasStrValue(string customKey)
        {
            return GetStrValueInfoMap().ContainsKey(customKey);
        }

        public ValueData[] GetValueDataList(TangentValueType valueType)
        {
            return GetOrBuildList(ref _valueDataListCache, valueType, BuildValueDataList);
        }

        private ValueData[] BuildValueDataList(TangentValueType valueType)
        {
            switch (valueType)
            {
                case TangentValueType.X移動:
                    if (hasPosition)
                    {
                        return new ValueData[] { positionValues[0] };
                    }
                    break;
                case TangentValueType.Y移動:
                    if (hasPosition)
                    {
                        return new ValueData[] { positionValues[1] };
                    }
                    break;
                case TangentValueType.Z移動:
                    if (hasPosition)
                    {
                        return new ValueData[] { positionValues[2] };
                    }
                    break;
                case TangentValueType.移動:
                    if (hasPosition)
                    {
                        return positionValues;
                    }
                    break;
                case TangentValueType.X回転:
                    if (hasRotation)
                    {
                        return new ValueData[] { rotationValues[0] };
                    }
                    if (hasEulerAngles)
                    {
                        return new ValueData[] { eulerAnglesValues[0] };
                    }
                    break;
                case TangentValueType.Y回転:
                    if (hasRotation)
                    {
                        return new ValueData[] { rotationValues[1] };
                    }
                    if (hasEulerAngles)
                    {
                        return new ValueData[] { eulerAnglesValues[1] };
                    }
                    break;
                case TangentValueType.Z回転:
                    if (hasRotation)
                    {
                        return new ValueData[] { rotationValues[2] };
                    }
                    if (hasEulerAngles)
                    {
                        return new ValueData[] { eulerAnglesValues[2] };
                    }
                    break;
                case TangentValueType.W回転:
                    if (hasRotation)
                    {
                        return new ValueData[] { rotationValues[3] };
                    }
                    break;
                case TangentValueType.回転:
                    if (hasRotation)
                    {
                        return rotationValues;
                    }
                    if (hasEulerAngles)
                    {
                        return eulerAnglesValues;
                    }
                    break;
                case TangentValueType.X拡縮:
                    if (hasScale)
                    {
                        return new ValueData[] { scaleValues[0] };
                    }
                    break;
                case TangentValueType.Y拡縮:
                    if (hasScale)
                    {
                        return new ValueData[] { scaleValues[1] };
                    }
                    break;
                case TangentValueType.Z拡縮:
                    if (hasScale)
                    {
                        return new ValueData[] { scaleValues[2] };
                    }
                    break;
                case TangentValueType.拡縮:
                    if (hasScale)
                    {
                        return scaleValues;
                    }
                    break;
                case TangentValueType.すべて:
                    return tangentValues;
            }

            return new ValueData[0];
        }

        public TangentData[] GetInTangentDataList(TangentValueType valueType)
        {
            return GetOrBuildList(ref _inTangentListCache, valueType, BuildInTangentDataList);
        }

        public TangentData[] GetOutTangentDataList(TangentValueType valueType)
        {
            return GetOrBuildList(ref _outTangentListCache, valueType, BuildOutTangentDataList);
        }

        private TangentData[] BuildInTangentDataList(TangentValueType valueType)
        {
            var dataList = GetValueDataList(valueType);
            var result = new TangentData[dataList.Length];
            for (int i = 0; i < dataList.Length; i++)
            {
                result[i] = dataList[i].inTangent;
            }
            return result;
        }

        private TangentData[] BuildOutTangentDataList(TangentValueType valueType)
        {
            var dataList = GetValueDataList(valueType);
            var result = new TangentData[dataList.Length];
            for (int i = 0; i < dataList.Length; i++)
            {
                result[i] = dataList[i].outTangent;
            }
            return result;
        }

        public virtual void Reset()
        {
            if (hasPosition)
            {
                position = initialPosition;
            }
            if (hasSubPosition)
            {
                subPosition = initialSubPosition;
            }
            if (hasRotation)
            {
                rotation = initialRotation;
            }
            if (hasSubRotation)
            {
                subRotation = initialSubRotation;
            }
            if (hasEulerAngles)
            {
                eulerAngles = initialEulerAngles;
            }
            if (hasSubEulerAngles)
            {
                subEulerAngles = initialSubEulerAngles;
            }
            if (hasScale)
            {
                scale = initialScale;
            }
            if (hasVisible)
            {
                visible = initialVisible;
            }
            if (hasEasingChannel)
            {
                easing = 0;
            }

            foreach (var pair in GetColorValueInfoMap())
            {
                SetColorValue(pair.Key, pair.Value.defaultValue);
            }

            foreach (var customKey in GetCustomValueInfoMap().Keys)
            {
                GetCustomValue(customKey).value = GetDefaultCustomValue(customKey);
            }

            foreach (var strKey in GetStrValueInfoMap().Keys)
            {
                SetStrValue(strKey, string.Empty);
            }
        }

        public ITransformData Clone()
        {
            var clone = (TransformDataBase) MemberwiseClone();

            // 浅いコピーでは元インスタンスの ValueData を指すキャッシュを引き継いでしまう
            clone.ClearValueDataListCache();

            clone._values = new ValueData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                clone._values[i] = values[i].Clone();
            }

            clone._strValues = new string[strValues.Length];
            for (int i = 0; i < strValues.Length; i++)
            {
                clone._strValues[i] = strValues[i];
            }

            return clone;
        }

        public override bool Equals(object obj)
        {
            var other = obj as TransformDataBase;
            if (other == null)
            {
                return false;
            }

            if (name != other.name)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].Equals(other.values[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < strValues.Length; i++)
            {
                if (strValues[i] != other.strValues[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + name.GetHashCode();
                hash = hash * 23 + values.GetHashCode();
                hash = hash * 23 + strValues.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            var ret = "TransformData";
            ret += string.Format(" name={0} ", name);
            if (valueCount > 0)
            {
                ret += " values=";
                ret += string.Join(", ", values.Select(value => value.value.ToString()).ToArray());
            }
            if (strValueCount > 0)
            {
                ret += " strValues=";
                ret += string.Join(", ", strValues.Select(value => value).ToArray());
            }
            return ret;
        }
    }
}