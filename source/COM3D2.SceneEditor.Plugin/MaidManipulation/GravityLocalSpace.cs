using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 重力のローカル座標（Bip01 基準）の計算。
    /// 立ちポーズでワールドと一致し、体を倒すと重力も体に合わせて回るよう、
    /// Bip01 の回転から基準姿勢の回転を除いた分だけ offset を回す
    /// </summary>
    public static class GravityLocalSpace
    {
        /// <summary>
        /// メイドのルート回転が単位のときの Bip01 の基準姿勢（ワールド回転）。
        /// エディットの標準の立ちポーズ maid_stand01.anm の 0 秒を旧ボディで実測した値。
        /// 骨盤の傾きはポーズごとに違うため、ほかの立ちポーズではその分だけワールドからずれる
        /// </summary>
        public static readonly Quaternion Bip01BaseRotation =
            new Quaternion(-0.5415668f, 0.5415668f, 0.4546487f, 0.4546487f);

        // Quaternion.Inverse はネイティブ呼び出しでテストから使えないため、単位クォータニオンの共役で逆回転を作る
        private static readonly Quaternion Bip01BaseInverse = new Quaternion(
            -Bip01BaseRotation.x, -Bip01BaseRotation.y, -Bip01BaseRotation.z, Bip01BaseRotation.w);

        /// <summary>基準姿勢からの体の回転</summary>
        public static Quaternion GetBodyRotation(Quaternion bip01Rotation)
        {
            return bip01Rotation * Bip01BaseInverse;
        }

        /// <summary>offset を体の回転に合わせて回し、ゲーム側の ±1 クランプで向きが崩れない範囲へ収める</summary>
        public static Vector3 ToForce(Quaternion bip01Rotation, Vector3 offset)
        {
            return FitToUnitBox(GetBodyRotation(bip01Rotation) * offset);
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
