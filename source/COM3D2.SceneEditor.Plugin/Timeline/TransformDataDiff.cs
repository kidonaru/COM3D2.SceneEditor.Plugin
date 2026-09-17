using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 差分キーフレーム登録用の「変化なし」判定。
    /// 固定 IK は編集モード中に毎フレームチェーンを解き直すため、触っていないボーンでも
    /// 四元数の符号反転 (q と -q) や浮動小数ノイズが編集開始スナップショットとの間に乗る。
    /// TransformDataBase.Equals の完全一致で比べると無関係なボーンまで登録されるので、
    /// 回転は角度差、他の値は許容誤差で比較する
    /// </summary>
    public static class TransformDataDiff
    {
        /// <summary>
        /// 回転を同じとみなす角度差 (度)。実機で観測した FABRIK の再解ノイズは 1e-5 度未満で、
        /// 人が操作した差 (最小でも 0.01 度台) とは桁が離れているため、余裕を見て 100 倍の値にしている
        /// </summary>
        private const float ROTATION_ANGLE_EPSILON = 0.001f;

        /// <summary>回転以外の値を同じとみなす差。観測ノイズ (1e-7 級) に対する余裕込み</summary>
        private const float VALUE_EPSILON = 1e-5f;

        public static bool IsApproximatelyEqual(ITransformData a, ITransformData b)
        {
            if (a.name != b.name)
            {
                return false;
            }

            // 回転成分を values ループから除外するための一覧。rotationValues は values の要素と
            // 参照を共有した配列なので参照同一で引く (ValueData.Equals は値比較のため、
            // 値で引くと回転成分と同じ値を持つ位置成分の変化を見落とす)
            var rotationValues = new List<ValueData>();
            if (a.hasRotation)
            {
                if (Quaternion.Angle(a.rotation, b.rotation) > ROTATION_ANGLE_EPSILON)
                {
                    return false;
                }
                rotationValues.AddRange(a.rotationValues);
            }
            if (a.hasSubRotation)
            {
                if (Quaternion.Angle(a.subRotation, b.subRotation) > ROTATION_ANGLE_EPSILON)
                {
                    return false;
                }
                rotationValues.AddRange(a.subRotationValues);
            }

            var values = a.values;
            var otherValues = b.values;
            if (values.Length != otherValues.Length)
            {
                return false;
            }
            for (var i = 0; i < values.Length; i++)
            {
                // 回転成分は上で角度として比較済み
                if (ContainsReference(rotationValues, values[i]))
                {
                    continue;
                }
                if (Mathf.Abs(values[i].value - otherValues[i].value) > VALUE_EPSILON)
                {
                    return false;
                }
            }

            var strValues = a.strValues;
            var otherStrValues = b.strValues;
            if (strValues.Length != otherStrValues.Length)
            {
                return false;
            }
            for (var i = 0; i < strValues.Length; i++)
            {
                if (strValues[i] != otherStrValues[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsReference(List<ValueData> list, ValueData target)
        {
            foreach (var item in list)
            {
                if (ReferenceEquals(item, target))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
