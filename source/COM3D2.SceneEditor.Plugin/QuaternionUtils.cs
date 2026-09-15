using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// クォータニオンの生成・補間を純 C# で行うユーティリティ。
    ///
    /// <see cref="Quaternion.Euler"/> / <see cref="Quaternion.Slerp"/> / <see cref="Quaternion.eulerAngles"/> は
    /// Unity のネイティブ ECall で、単体テストのプロセスからは呼ぶと SecurityException になる。
    /// XML のバージョン移行やキー間補間のように「静かに壊れると既存データが失われる」経路は
    /// テストで固定したいので、これらだけは managed 実装を自前で持つ。
    /// 挙動は Unity 実装と一致させること（回転順は Unity と同じ ZXY = <c>qY * qX * qZ</c>）
    /// </summary>
    public static class QuaternionUtils
    {
        /// <summary>
        /// オイラー角（度）からクォータニオンを作る。<see cref="Quaternion.Euler"/> と同値。
        /// 各軸の基本クォータニオンを Unity と同じ ZXY 順で合成する
        /// （成分ごとの展開式を書き下すより、合成順の誤りが目で追えるため）
        /// </summary>
        public static Quaternion EulerToQuaternion(Vector3 eulerAngles)
        {
            var halfX = eulerAngles.x * 0.5f * Mathf.Deg2Rad;
            var halfY = eulerAngles.y * 0.5f * Mathf.Deg2Rad;
            var halfZ = eulerAngles.z * 0.5f * Mathf.Deg2Rad;

            var qx = new Quaternion(Mathf.Sin(halfX), 0f, 0f, Mathf.Cos(halfX));
            var qy = new Quaternion(0f, Mathf.Sin(halfY), 0f, Mathf.Cos(halfY));
            var qz = new Quaternion(0f, 0f, Mathf.Sin(halfZ), Mathf.Cos(halfZ));

            return qy * qx * qz;
        }

        /// <summary>
        /// 球面線形補間。<see cref="Quaternion.Slerp"/> と同値で、t は [0, 1] へ丸め、
        /// 内積が負なら終点の符号を反転して最短経路を通る
        /// </summary>
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            return SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }

        /// <summary>t を丸めない <see cref="Slerp"/></summary>
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            a = Normalize(a);
            b = Normalize(b);

            var dot = Quaternion.Dot(a, b);
            if (dot < 0f)
            {
                // 遠回りの経路になるので終点側を反転する。回転としては同一の向き
                b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
                dot = -dot;
            }

            // ほぼ同じ向きだと sin(theta) が 0 に近づいて割り算が破綻するため、
            // 線形補間 + 正規化 (nlerp) へ落とす。この範囲では両者の差は誤差以下
            const float lerpThreshold = 0.9995f;
            if (dot > lerpThreshold)
            {
                return Normalize(new Quaternion(
                    a.x + (b.x - a.x) * t,
                    a.y + (b.y - a.y) * t,
                    a.z + (b.z - a.z) * t,
                    a.w + (b.w - a.w) * t));
            }

            var theta = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            var sinTheta = Mathf.Sin(theta);
            var scaleA = Mathf.Sin((1f - t) * theta) / sinTheta;
            var scaleB = Mathf.Sin(t * theta) / sinTheta;

            return new Quaternion(
                a.x * scaleA + b.x * scaleB,
                a.y * scaleA + b.y * scaleB,
                a.z * scaleA + b.z * scaleB,
                a.w * scaleA + b.w * scaleB);
        }

        /// <summary>
        /// 単位長へ正規化する。<see cref="Quaternion.Normalize"/> は managed なので本来は不要だが、
        /// 全成分がゼロへ潰れた入力を identity へ倒す扱いをここへ集約する
        /// </summary>
        public static Quaternion Normalize(Quaternion q)
        {
            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (magnitude < 1e-6f)
            {
                return Quaternion.identity;
            }

            return new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude);
        }
    }
}
