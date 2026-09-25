using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カーブビューのスクリーン座標⇔値のマッピング (純粋ロジック、単体テスト対象)。
    /// 横軸はドープシートとフレームスケールを共有し、縦軸は表示値域に自動フィットする
    /// (手動ズーム・パン中は ZoomValue / PanValue で作った値域を使う)
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

        /// <summary>縦ズームで狭められる値域の下限。これより狭いと float の精度で曲線が崩れる</summary>
        public const float MinValueRange = 1e-4f;
        /// <summary>縦ズームで広げられる値域の上限。これより広いと曲線が 1px に潰れて意味がない</summary>
        public const float MaxValueRange = 1e6f;

        /// <summary>
        /// 縦ズーム。pivotY の値を画面上で固定したまま値域を 1/factor 倍にする。
        /// 値域が MinValueRange〜MaxValueRange を外れる拡縮は行わず、そのままの値域を返す
        /// </summary>
        public CurveViewMapping ZoomValue(float factor, float pivotY)
        {
            var newRange = (valueMax - valueMin) / factor;
            if (newRange < MinValueRange || newRange > MaxValueRange)
            {
                return this;
            }

            var pivotValue = YToValue(pivotY);
            var newMin = pivotValue - (pivotValue - valueMin) / factor;
            var newMax = pivotValue + (valueMax - pivotValue) / factor;
            return new CurveViewMapping(frameWidth, paneHeight, newMin, newMax);
        }

        /// <summary>縦パン。下へ dyPx ドラッグしたら曲線も下へ付いてくるよう値域を上へずらす</summary>
        public CurveViewMapping PanValue(float dyPx)
        {
            var delta = dyPx * (valueMax - valueMin) / paneHeight;
            return new CurveViewMapping(frameWidth, paneHeight, valueMin + delta, valueMax + delta);
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
