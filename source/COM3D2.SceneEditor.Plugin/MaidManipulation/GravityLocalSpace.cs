using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力のローカル座標（カテゴリの基準ボーン基準）の計算。
    /// 立ちポーズでワールドと一致し、基準ボーンを傾けると重力も合わせて回るよう、
    /// 基準ボーンの回転から基準姿勢の回転を除いた分だけ offset を回す
    /// </summary>
    public static class GravityLocalSpace
    {
        // 基準姿勢は、メイドのルート回転が単位のときの各ボーンのワールド回転。
        // エディットの標準の立ちポーズ maid_stand01.anm の 0 秒を旧ボディで実測した値（CRC ボディも同じ値）。
        // 傾きはポーズごとに違うため、ほかの立ちポーズではその分だけワールドからずれる

        /// <summary>髪の基準ボーン（Bip01 Head）の基準姿勢</summary>
        public static readonly Quaternion HeadBaseRotation =
            new Quaternion(0.5777411f, -0.4202374f, -0.5255319f, 0.4619873f);

        /// <summary>スカートの基準ボーン（Bip01 Pelvis）の基準姿勢</summary>
        public static readonly Quaternion PelvisBaseRotation =
            new Quaternion(0.5018583f, -0.4970104f, -0.5237849f, 0.4762020f);

        /// <summary>基準姿勢からのボーンの回転</summary>
        public static Quaternion GetBoneRotation(Quaternion boneRotation, Quaternion baseRotation)
        {
            // Quaternion.Inverse はネイティブ呼び出しでテストから使えないため、単位クォータニオンの共役で逆回転を作る
            var baseInverse = new Quaternion(-baseRotation.x, -baseRotation.y, -baseRotation.z, baseRotation.w);
            return boneRotation * baseInverse;
        }

        /// <summary>offset をボーンの回転に合わせて回し、ゲーム側の ±1 クランプで向きが崩れない範囲へ収める</summary>
        public static Vector3 ToForce(Quaternion boneRotation, Quaternion baseRotation, Vector3 offset)
        {
            return FitToUnitBox(GetBoneRotation(boneRotation, baseRotation) * offset);
        }

        /// <summary>
        /// いずれかの成分の絶対値が 1 を超えたら、最大成分が 1 になるよう全体を縮める。
        /// GravityTransformControl は成分ごとに ±1 へ切り詰めるため、回転後の値をそのまま渡すと方向が変わる
        /// </summary>
        public static Vector3 FitToUnitBox(Vector3 v)
        {
            var max = Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
            if (max <= 1f)
            {
                return v;
            }
            return v / max;
        }
    }
}
