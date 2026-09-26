#ifndef SE_PNG_COMMON_INCLUDED
#define SE_PNG_COMMON_INCLUDED

#include "UnityCG.cginc"

// PNG 配置 (板・デカール) の共通処理。
// 値の意味は C# の PngPlacementManager / PngBlendMode と対応する

// 彩度。0 でグレースケール、1 で元の色、1 より大きいと強調する。
// 範囲外の値でも出力を 0〜1 に収める
inline fixed3 PngApplySaturation(fixed3 rgb, float saturation)
{
    fixed luma = Luminance(rgb);
    return saturate(lerp(fixed3(luma, luma, luma), rgb, saturation));
}

// _BlendMode 1 = PngBlendMode.Multiply (DstColor Zero): 透明な所は白 (変化なし) へ寄せる
inline fixed4 PngApplyMultiply(fixed4 col, float blendMode)
{
    if (blendMode > 0.5 && blendMode < 1.5)
    {
        return fixed4(lerp(fixed3(1, 1, 1), col.rgb, col.a), 1);
    }
    return col;
}

// オーバーレイ合成 (Photoshop と同じ式)。base が下地、blend が画像の色
inline fixed3 PngOverlay(fixed3 base, fixed3 blend)
{
    fixed3 low = 2 * base * blend;
    fixed3 high = 1 - 2 * (1 - base) * (1 - blend);
    return lerp(low, high, step(0.5, base));
}

#endif
