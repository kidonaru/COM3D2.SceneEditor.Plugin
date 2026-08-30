using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カーブビューのスクリーン座標⇔値のマッピング (純粋ロジック、単体テスト対象)。
    /// 横軸はドープシートとフレームスケールを共有し、縦軸は表示値域に自動フィットする
    /// </summary>
    public class CurveViewMapping
    {
        public float frameWidth { get; private set; }
        public float paneHeight { get; private set; }
        public float valueMin { get; private set; }
        public float valueMax { get; private set; }

        public CurveViewMapping(float frameWidth, float paneHeight, float valueMin, float valueMax)
        {
            this.frameWidth = frameWidth;
            this.paneHeight = paneHeight;
            this.valueMin = valueMin;
            this.valueMax = valueMax;
        }

        public float FrameToX(float frameNo)
        {
            // ドープシートのキー描画に合わせてフレーム中心へ置く
            return frameNo * frameWidth + frameWidth * 0.5f;
        }

        public float ValueToY(float value)
        {
            var range = valueMax - valueMin;
            return paneHeight * (1f - (value - valueMin) / range);
        }

        public float YToValue(float y)
        {
            var range = valueMax - valueMin;
            return valueMin + (1f - y / paneHeight) * range;
        }

        /// <summary>スクリーン上の移動量 (px) を「1フレームあたりの値変化量」へ変換</summary>
        public float ScreenSlopeToValueSlope(float dxPx, float dyPx)
        {
            if (dxPx == 0f)
            {
                return 0f;
            }
            var valuePerPx = (valueMax - valueMin) / paneHeight;
            var framePerPx = 1f / frameWidth;
            return (-dyPx * valuePerPx) / (dxPx * framePerPx);
        }

        public static CurveViewMapping AutoFit(
            float frameWidth,
            float paneHeight,
            IEnumerable<float> sampledValues)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var v in sampledValues)
            {
                if (float.IsNaN(v)) continue;
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            if (min > max)
            {
                min = 0f;
                max = 1f;
            }

            var range = max - min;
            if (range <= 0f)
            {
                // 値域ゼロ (全キー同値) は中央表示になるよう固定幅を与える
                range = Mathf.Max(1f, Mathf.Abs(min) * 0.2f);
                min -= range * 0.5f;
                max += range * 0.5f;
                return new CurveViewMapping(frameWidth, paneHeight, min, max);
            }

            // 上下 10% 余白
            return new CurveViewMapping(
                frameWidth, paneHeight, min - range * 0.1f, max + range * 0.1f);
        }
    }
}
