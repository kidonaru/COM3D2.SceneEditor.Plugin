using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 追加ライトの影の種類。XML には LightShadows の数値 (0=なし / 1=ハード / 2=ソフト) で保存する
    /// </summary>
    public static class LightShadowValues
    {
        /// <summary>XML など外部入力由来の値を丸める。範囲外は影なし</summary>
        public static LightShadows FromInt(int value)
        {
            switch (value)
            {
                case (int)LightShadows.Hard:
                    return LightShadows.Hard;
                case (int)LightShadows.Soft:
                    return LightShadows.Soft;
                default:
                    return LightShadows.None;
            }
        }
    }
}
