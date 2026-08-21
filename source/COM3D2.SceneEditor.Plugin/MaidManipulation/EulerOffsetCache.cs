using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 基準回転からのオフセット角を、表示の連続性を保って分解するキャッシュ。
    /// quaternion からの再分解は X±90° 超で別表現 (X反転 + Y/Z 180°) に飛び
    /// スライダーが暴れるため、
    /// - 自分で書き込んだ直後は書き込んだ表現をそのまま返す
    /// - ギズモ等の外部操作で回転が変わったときは、等価な 2 表現のうち
    ///   前回返した値に近い方を選んで連続させる
    /// スライダー操作は同時に 1 対象のみのため、直近の 1 件だけを保持する
    /// </summary>
    public class EulerOffsetCache
    {
        private Transform _target;
        private bool _useLocal;
        private Quaternion _baseRot;
        private Quaternion _rot;
        private Vector3 _euler;

        /// <summary>基準回転からのオフセット角（±180 正規化済み）を連続性を保って返す</summary>
        public Vector3 GetOffset(Transform target, Quaternion baseRot, bool useLocal = true)
        {
            var rot = useLocal ? target.localRotation : target.rotation;
            var sameTarget = target == _target && useLocal == _useLocal && baseRot == _baseRot;
            if (sameTarget && rot == _rot)
            {
                return _euler;
            }

            // オフセットの合成順は座標系で異なる:
            // Local は基準の子として回す (rot = base * offset)、
            // Global はワールド軸で基準へ前掛けする (rot = offset * base)
            var offsetQ = useLocal
                ? Quaternion.Inverse(baseRot) * rot
                : rot * Quaternion.Inverse(baseRot);
            var raw = offsetQ.eulerAngles;
            var euler = new Vector3(
                NormalizeAngle(raw.x),
                NormalizeAngle(raw.y),
                NormalizeAngle(raw.z));

            if (sameTarget)
            {
                // 等価表現 (180-X, Y+180, Z+180) のうち前回に近い方を選ぶ
                var alt = new Vector3(
                    NormalizeAngle(180f - euler.x),
                    NormalizeAngle(euler.y + 180f),
                    NormalizeAngle(euler.z + 180f));
                if (DiffScore(alt, _euler) < DiffScore(euler, _euler))
                {
                    euler = alt;
                }
            }

            _target = target;
            _useLocal = useLocal;
            _baseRot = baseRot;
            _rot = rot;
            _euler = euler;
            return euler;
        }

        /// <summary>書き込んだオイラー表現を記録する (target の回転設定後に呼ぶ)</summary>
        public void Store(Transform target, Quaternion baseRot, Vector3 euler, bool useLocal = true)
        {
            _target = target;
            _useLocal = useLocal;
            _baseRot = baseRot;
            _rot = useLocal ? target.localRotation : target.rotation;
            _euler = new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        public void Clear()
        {
            _target = null;
            _baseRot = Quaternion.identity;
            _rot = Quaternion.identity;
            _euler = Vector3.zero;
        }

        /// <summary>角度差を ±180 の巡回を考慮して比較するためのスコア</summary>
        private static float DiffScore(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x))
                + Mathf.Abs(Mathf.DeltaAngle(a.y, b.y))
                + Mathf.Abs(Mathf.DeltaAngle(a.z, b.z));
        }

        /// <summary>ローカル基準回転をワールドへ持ち上げる (Global 表示用の基準)</summary>
        public static Quaternion WorldBaseRotation(Transform target, Quaternion baseLocalRot)
        {
            return target.parent != null
                ? target.parent.rotation * baseLocalRot
                : baseLocalRot;
        }

        /// <summary>
        /// ローカル基準回転を起点に、Local/Global を吸収してオフセットを返す。
        /// Global はワールドへ持ち上げた基準からワールド軸で分解する
        /// </summary>
        public Vector3 GetOffsetFromLocalBase(Transform target, Quaternion baseLocalRot, bool useLocal)
        {
            return useLocal
                ? GetOffset(target, baseLocalRot)
                : GetOffset(target, WorldBaseRotation(target, baseLocalRot), false);
        }

        /// <summary>
        /// 指定軸のオフセット角を書き込む。他軸は現在値を維持し、
        /// 書き込んだ表現をキャッシュへ記録する
        /// </summary>
        public void SetOffsetAxisFromLocalBase(
            Transform target, Quaternion baseLocalRot, int axisIndex, float value, bool useLocal)
        {
            var offset = GetOffsetFromLocalBase(target, baseLocalRot, useLocal);
            offset[axisIndex] = value;
            SetOffsetFromLocalBase(target, baseLocalRot, offset, useLocal);
        }

        /// <summary>
        /// オフセット角を 3 軸まとめて書き込み、書き込んだ表現をキャッシュへ記録する
        /// </summary>
        public void SetOffsetFromLocalBase(
            Transform target, Quaternion baseLocalRot, Vector3 offset, bool useLocal)
        {
            if (useLocal)
            {
                target.localRotation = baseLocalRot * Quaternion.Euler(offset);
                Store(target, baseLocalRot, offset);
            }
            else
            {
                var baseWorld = WorldBaseRotation(target, baseLocalRot);
                target.rotation = Quaternion.Euler(offset) * baseWorld;
                Store(target, baseWorld, offset, false);
            }
        }

        /// <summary>角度を -180〜180 に正規化する</summary>
        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
