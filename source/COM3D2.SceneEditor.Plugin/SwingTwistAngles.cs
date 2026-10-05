using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 回転を「傾き + ひねり」の 3 値で表す。Y 軸に伸びる棒（サイリウム）の向きを編集するためのもの。
    ///
    /// Unity のオイラー角（ZXY = <c>qY * qX * qZ</c>）は X = ±90° で Z が Y と同じ軸になり、
    /// 棒を前へ水平に倒した姿勢で「Z = 左右へ傾ける」が効かなくなる。
    /// この表現では x / z を傾きベクトル（向き = XZ 平面上の回転軸、長さ = 傾ける角度）、
    /// y を棒の軸まわりのひねりとし、<c>swing * twist</c> で合成する。
    /// 表現が定まらないのは棒が真下を向く（傾き 180°）ときだけ。
    /// 単軸だけの値は Unity のオイラー角と同じ姿勢になる。
    /// テストから呼べるよう、Unity のネイティブ ECall は使わない
    /// </summary>
    public static class SwingTwistAngles
    {
        private const float Epsilon = 1e-6f;

        public static Quaternion ToQuaternion(Vector3 angles)
        {
            var swing = Quaternion.identity;
            var swingAngle = Mathf.Sqrt(angles.x * angles.x + angles.z * angles.z);
            if (swingAngle > Epsilon)
            {
                var halfSwing = swingAngle * 0.5f * Mathf.Deg2Rad;
                // 軸 (x, 0, z) / swingAngle の正規化と sin(θ/2) を 1 つの係数にまとめる
                var scale = Mathf.Sin(halfSwing) / swingAngle;
                swing = new Quaternion(angles.x * scale, 0f, angles.z * scale, Mathf.Cos(halfSwing));
            }

            var halfTwist = angles.y * 0.5f * Mathf.Deg2Rad;
            var twist = new Quaternion(0f, Mathf.Sin(halfTwist), 0f, Mathf.Cos(halfTwist));

            return swing * twist;
        }

        public static Vector3 FromQuaternion(Quaternion rotation)
        {
            var q = QuaternionUtils.Normalize(rotation);

            // ひねりは q を Y 軸へ射影したもの。真下向きでは射影が 0 になり定まらないので 0 とする
            var twist = Quaternion.identity;
            var twistLength = Mathf.Sqrt(q.y * q.y + q.w * q.w);
            if (twistLength > Epsilon)
            {
                twist = new Quaternion(0f, q.y / twistLength, 0f, q.w / twistLength);
            }

            // swing = q * twist^-1。y 成分は構成上 0 になる
            var swing = q * new Quaternion(0f, -twist.y, 0f, twist.w);

            // 傾き角を [0, 180] に収めるため swing の w を非負にそろえる。
            // 両方の符号を反転するので合成した姿勢は変わらない
            if (swing.w < 0f)
            {
                swing = new Quaternion(-swing.x, -swing.y, -swing.z, -swing.w);
                twist = new Quaternion(0f, -twist.y, 0f, -twist.w);
            }

            var x = 0f;
            var z = 0f;
            var sinHalfSwing = Mathf.Sqrt(swing.x * swing.x + swing.z * swing.z);
            if (sinHalfSwing > Epsilon)
            {
                var swingAngle = 2f * Mathf.Atan2(sinHalfSwing, swing.w) * Mathf.Rad2Deg;
                x = swing.x / sinHalfSwing * swingAngle;
                z = swing.z / sinHalfSwing * swingAngle;
            }

            var twistAngle = 2f * Mathf.Atan2(twist.y, twist.w) * Mathf.Rad2Deg;
            return new Vector3(x, Mathf.DeltaAngle(0f, twistAngle), z);
        }
    }
}
