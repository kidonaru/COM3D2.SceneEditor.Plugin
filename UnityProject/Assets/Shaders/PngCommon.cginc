#ifndef SE_PNG_COMMON_INCLUDED
#define SE_PNG_COMMON_INCLUDED

#include "UnityCG.cginc"

// PNG 配置 (板・デカール) の共通処理。
// 値の意味は C# の PngPlacementManager / PngBlendMode と対応する

// 彩度。0 でグレースケール、1 で元の色、1 より大きいと強調する。
// 明るさで 1 を超えた色は加算などの合成で効くため、上限は切らず負にならないようにだけ抑える
inline fixed3 PngApplySaturation(fixed3 rgb, float saturation)
{
    fixed luma = Luminance(rgb);
    return max(0, lerp(fixed3(luma, luma, luma), rgb, saturation));
}

// ブレンド方式に合わせて出力を整える。
// 乗算 (_BlendMode 1 = PngBlendMode.Multiply, DstColor Zero) のときだけ透明な所を白 (変化なし) へ寄せ、
// それ以外はそのまま返す
inline fixed4 PngApplyBlendOutput(fixed4 col, float blendMode)
{
    if (blendMode > 0.5 && blendMode < 1.5)
    {
        return fixed4(lerp(fixed3(1, 1, 1), col.rgb, col.a), 1);
    }
    return col;
}

// オーバーレイ合成 (Photoshop と同じ式)。base が下地、blend が画像の色。
// 式の定義域 (0〜1) に合わせ、HDR の下地や明るさで 1 を超えた色は切る
inline fixed3 PngOverlay(fixed3 base, fixed3 blend)
{
    base = saturate(base);
    blend = saturate(blend);
    fixed3 low = 2 * base * blend;
    fixed3 high = 1 - 2 * (1 - base) * (1 - blend);
    return lerp(low, high, step(0.5, base));
}

#endif
