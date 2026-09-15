using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>角度表示の共通処理</summary>
    public static class AngleUtils
    {
        /// <summary>
        /// 角度を (-180, 180] へ正規化する。
        /// 旋回を続けると角度は際限なく積み上がり、quaternion から取り出した角度は
        /// 0〜360 に丸まるため、スライダー表示の前にこれを通す
        /// </summary>
        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }

        /// <summary>成分ごとに <see cref="NormalizeAngle"/> を適用する</summary>
        public static Vector3 NormalizeAngles(Vector3 angles)
        {
            return new Vector3(
                NormalizeAngle(angles.x),
                NormalizeAngle(angles.y),
                NormalizeAngle(angles.z));
        }

        /// <summary>
        /// 前の角度との差が ±180 度以内になるよう、360 度単位で寄せる。
        /// キー間の補間で遠回りの経路を選ばせないための補正。
        /// 差がちょうど ±180 度のときは寄せない（どちらへ回っても等距離のため）。
        /// 集約前の実装は差を <c>(int)</c> で切り捨てており 180.0〜181.0 度の帯で補正が発火しなかったが、
        /// <see cref="Mathf.Round"/> ベースへ揃えて解消した。
        /// 差が 180 度の奇数倍ちょうどのときだけ、丸めの偶数寄せで結果の符号が旧実装と異なりうる
        /// （±180 度は同一の回転なので実害はない）
        /// </summary>
        public static float GetFixedAngle(float angle, float prevAngle)
        {
            return angle - Mathf.Round((angle - prevAngle) / 360f) * 360f;
        }

        /// <summary>成分ごとに <see cref="GetFixedAngle"/> を適用する</summary>
        public static Vector3 GetFixedAngles(Vector3 angles, Vector3 prevAngles)
        {
            return new Vector3(
                GetFixedAngle(angles.x, prevAngles.x),
                GetFixedAngle(angles.y, prevAngles.y),
                GetFixedAngle(angles.z, prevAngles.z));
        }
    }
}
