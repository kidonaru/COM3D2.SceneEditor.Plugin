using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>骨 1 本に掛ける量。複数の項目が同じ骨に掛かる場合は合成済み</summary>
    public struct BodySliderBoneOp
    {
        public Vector3 scale;
        public Vector3 offset;
        public bool hasScale;
        public bool hasOffset;
    }

    /// <summary>
    /// メイド 1 体分の体型スライダーの値。未設定の項目は既定値 (スケール 1、位置 0)。
    /// 既定値は保持しないので、全項目が既定なら isDefault になる
    /// </summary>
    public class BodySliderState
    {
        private readonly Dictionary<string, Vector3> _values = new Dictionary<string, Vector3>();

        public bool isDefault => _values.Count == 0;

        /// <summary>既定値でない項目だけ</summary>
        public IEnumerable<KeyValuePair<string, Vector3>> nonDefaultValues => _values;

        public Vector3 Get(string key)
        {
            var item = BodySliderDefs.Find(key);
            if (item == null)
            {
                return Vector3.zero;
            }
            Vector3 values;
            return _values.TryGetValue(key, out values) ? values : item.defaultValues;
        }

        /// <summary>範囲へ丸めて保持する。定義に無いキーは無視する</summary>
        public void Set(string key, Vector3 values)
        {
            var item = BodySliderDefs.Find(key);
            if (item == null)
            {
                return;
            }
            if (item.IsDefault(values))
            {
                _values.Remove(key);
                return;
            }
            _values[key] = item.Clamp(values);
        }

        public void Clear()
        {
            _values.Clear();
        }

        /// <summary>
        /// 骨ごとに掛ける量を求める。1 本の骨に複数の項目が掛かる場合 (Hip_? など) は、
        /// MVP と同じくスケールを成分ごとの積、位置を和で合成する
        /// </summary>
        public void BuildBoneOps(Dictionary<string, BodySliderBoneOp> result)
        {
            result.Clear();
            foreach (var pair in _values)
            {
                var item = BodySliderDefs.Find(pair.Key);
                if (item == null)
                {
                    continue;
                }
                foreach (var target in item.targets)
                {
                    BodySliderBoneOp op;
                    if (!result.TryGetValue(target.boneName, out op))
                    {
                        op = new BodySliderBoneOp { scale = Vector3.one, offset = Vector3.zero };
                    }

                    if (item.kind == BodySliderKind.Scale)
                    {
                        op.scale = Vector3.Scale(op.scale, item.ToScale(pair.Value));
                        op.hasScale = true;
                    }
                    else
                    {
                        op.offset += item.ToOffset(pair.Value, target);
                        op.hasOffset = true;
                    }
                    result[target.boneName] = op;
                }
            }
        }

        /// <summary>
        /// 退避値へ戻してよいか。今の値が自分の書いた値のままのときだけ戻す。
        /// 体型スライダー (BoneMorph_.Blend) や CopyTrans などが途中で書き直した値を古い退避値で潰さないため。
        /// Vector3 の == は近似比較 (差の 2 乗が 1e-10 未満) なので、それより小さな書き直しは区別しない
        /// </summary>
        public static bool ShouldRestore(Vector3 current, Vector3 written)
        {
            return current == written;
        }
    }
}
