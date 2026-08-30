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
    }
}
