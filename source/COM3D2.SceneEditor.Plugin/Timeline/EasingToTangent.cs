using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// Easing 関数を正規化 Tangent ペアへ近似変換する。
    ///
    /// 区間全体を最小二乗フィットして端点タンジェントを決める。
    /// 端点微分をそのまま採るより形状が保たれる (端点微分が発散する Circ 系では
    /// クランプした微分が大きく行き過ぎ、最大誤差が 1.0 に達して形状が破綻する)。
    /// InOut 系は端点勾配 0 の 3 次では中腹の急峻さを表現しきれず
    /// 最大 0.23 程度の誤差が残るが、全 MoveEasingType が単調なため
    /// 補間の向きが反転するような破綻は起きない
    /// </summary>
    public static class EasingToTangent
    {
        private const int SAMPLE_COUNT = 200;
        private const float MAX_TANGENT = 10f;

        public static TangentPair Convert(MoveEasingType easing)
        {
            // Hermite(v0=0, v1=1) は h01(t) + outTangent*h10(t) + inTangent*h11(t)。
            // 残差 f(t) - h01(t) を h10 / h11 の線形結合で近似する正規方程式を解く
            double a11 = 0.0, a12 = 0.0, a22 = 0.0, b1 = 0.0, b2 = 0.0;

            for (var i = 0; i <= SAMPLE_COUNT; i++)
            {
                double t = i / (double)SAMPLE_COUNT;
                double t2 = t * t;
                double t3 = t2 * t;
                double h01 = -2.0 * t3 + 3.0 * t2;
                double h10 = t3 - 2.0 * t2 + t;
                double h11 = t3 - t2;
                double residual = EasingFunctions.MoveEasing((float)t, easing) - h01;

                a11 += h10 * h10;
                a12 += h10 * h11;
                a22 += h11 * h11;
                b1 += h10 * residual;
                b2 += h11 * residual;
            }

            double det = a11 * a22 - a12 * a12;
            if (det == 0.0)
            {
                // 理論上到達しないが、退化したら線形補間へ倒す
                return new TangentPair { outTangent = 1f, inTangent = 1f, isSmooth = false };
            }

            return new TangentPair
            {
                outTangent = Mathf.Clamp((float)((b1 * a22 - b2 * a12) / det), 0f, MAX_TANGENT),
                inTangent = Mathf.Clamp((float)((a11 * b2 - a12 * b1) / det), 0f, MAX_TANGENT),
                isSmooth = false,
            };
        }
    }
}
