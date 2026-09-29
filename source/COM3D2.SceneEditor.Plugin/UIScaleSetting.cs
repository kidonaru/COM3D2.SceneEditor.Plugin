using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>設定ウィンドウで選べる UI 倍率の範囲と刻み (GUIScale の許容範囲の内側に置く)</summary>
    public static class UIScaleSetting
    {
        public const float Min = 0.8f;
        public const float Max = 2f;
        public const float Step = 0.05f;

        /// <summary>倍率を刻みへ丸めて範囲へ収める</summary>
        public static float Snap(float value)
        {
            return Mathf.Clamp(Mathf.Round(value / Step) * Step, Min, Max);
        }

        /// <summary>% 表記の値を刻みへ丸めて範囲へ収める (スライダー・入力欄の表示用)</summary>
        public static float SnapPercent(float percent)
        {
            return Mathf.Round(Snap(percent / 100f) * 100f);
        }
    }
}
