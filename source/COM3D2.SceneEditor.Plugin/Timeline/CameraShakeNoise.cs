using System;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのノイズ生成。
    /// 軸ごとにハッシュした周波数・位相の正弦波 3 本を重ねて手持ちカメラらしい揺れを作る。
    /// 経過秒だけを入力とする純粋関数なので、スクラブ・再生・動画出力で必ず同じ結果になる。
    /// Unity のネイティブ呼び出しを避けるため三角関数は System.Math を使う (テストが実行時に落ちるため)
    /// </summary>
    public static class CameraShakeNoise
    {
        /// <summary>位置 XYZ が軸 0-2、回転 XYZ が軸 3-5</summary>
        private const int RotationAxisOffset = 3;

        private const double TwoPi = Math.PI * 2.0;

        /// <summary>重ねる 3 本の正弦波の配合比 (合計 1.0)</summary>
        private const double Weight1 = 0.64;
        private const double Weight2 = 0.26;
        private const double Weight3 = 0.10;

        /// <summary>
        /// 指定軸のノイズ値 (-1〜1)。
        /// 基本周波数は 0.18〜0.36 / 0.40〜0.64 / 0.72〜1.06 Hz で、ゆっくりした手持ちの揺らぎになる
        /// </summary>
        public static float Sample(int axis, float seconds, int seed, float frequencyScale)
        {
            var f1 = 0.18 + Hash01(seed + axis * 1013 + 17) * 0.18;
            var f2 = 0.40 + Hash01(seed + axis * 2029 + 53) * 0.24;
            var f3 = 0.72 + Hash01(seed + axis * 4051 + 97) * 0.34;

            var p1 = Hash01(seed + axis * 8081 + 193) * TwoPi;
            var p2 = Hash01(seed + axis * 16001 + 389) * TwoPi;
            var p3 = Hash01(seed + axis * 32003 + 769) * TwoPi;

            var t = seconds * (double)frequencyScale;

            return (float)(
                Math.Sin(TwoPi * f1 * t + p1) * Weight1 +
                Math.Sin(TwoPi * f2 * t + p2) * Weight2 +
                Math.Sin(TwoPi * f3 * t + p3) * Weight3);
        }

        /// <summary>パラメータと経過秒から位置・回転のオフセットを求める</summary>
        public static void Evaluate(
            CameraShakeParams p, float seconds,
            out Vector3 positionOffset, out Vector3 eulerOffset)
        {
            // 周波数倍率 0 は位相が進まないだけで Sample はシード由来の非ゼロ定数を返す。
            // そのままだとカメラが固定量ずれたまま止まるので、揺れなしとして扱う
            if (p.isZero || p.frequencyScale <= 0f)
            {
                positionOffset = Vector3.zero;
                eulerOffset = Vector3.zero;
                return;
            }

            positionOffset = new Vector3(
                p.positionAmplitude.x * Sample(0, seconds, p.seed, p.frequencyScale),
                p.positionAmplitude.y * Sample(1, seconds, p.seed, p.frequencyScale),
                p.positionAmplitude.z * Sample(2, seconds, p.seed, p.frequencyScale));

            eulerOffset = new Vector3(
                p.rotationAmplitude.x * Sample(RotationAxisOffset + 0, seconds, p.seed, p.frequencyScale),
                p.rotationAmplitude.y * Sample(RotationAxisOffset + 1, seconds, p.seed, p.frequencyScale),
                p.rotationAmplitude.z * Sample(RotationAxisOffset + 2, seconds, p.seed, p.frequencyScale));
        }

        /// <summary>整数から 0〜1 の決定的な疑似乱数を作る (xorshift)</summary>
        private static double Hash01(int input)
        {
            var x = (uint)input;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return (x & 0xFFFFFF) / 16777215.0;
        }
    }
}
