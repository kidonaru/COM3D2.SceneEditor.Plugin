using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>設定ウィンドウで選べる UI 倍率の範囲と刻み</summary>
    public static class UIScaleSetting
    {
        public const float Min = 0.8f;
        public const float Max = 2f;
        public const float Step = 0.05f;

        /// <summary>スライダー・入力欄の値を刻みへ丸めて範囲へ収める</summary>
        public static float Snap(float value)
        {
            return Mathf.Clamp(Mathf.Round(value / Step) * Step, Min, Max);
        }
    }
}
