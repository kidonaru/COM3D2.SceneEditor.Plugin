#ifndef SE_PNG_DECAL_COMMON_INCLUDED
#define SE_PNG_DECAL_COMMON_INCLUDED

#include "PngCommon.cginc"

// SE/Decal と SE/DecalOverlay で共通の頂点処理と、合成前の色の計算

sampler2D _MainTex;
float4 _Color;
float _Saturation;
// ワールド → 投影箱 (各軸 -0.5〜0.5)。Projector 組込みの行列は
// エンジンのバージョンで名前が変わるため使わず、C# から渡す
float4x4 _DecalMatrix;
// 投影元の向き (root の +Z = 板の表側) のワールド方向
float4 _DecalNormal;
float _FadeCosMin;
float _FadeCosMax;

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
};

struct v2f
{
    float4 pos : SV_POSITION;
    float3 boxPos : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
    // オーバーレイ版だけが使う下地 (GrabPass) の座標
    float4 grabPos : TEXCOORD2;
};

v2f vert (appdata v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
    o.boxPos = mul(_DecalMatrix, worldPos).xyz;
    o.worldNormal = UnityObjectToWorldNormal(v.normal);
    o.grabPos = ComputeGrabScreenPos(o.pos);
    return o;
}

// 投影する画像の色 (彩度と角度フェードを適用済み)。箱の外は描かない
fixed4 PngDecalColor(v2f i)
{
    // 箱の外 (奥行き方向の突き抜けを含む) は描かない。0.5 は PngDecalProjection の箱の半幅
    clip(0.5 - abs(i.boxPos));

    // 投影元を向く面ほど濃く、横向きの面は消す (床から壁の側面への伸びを抑える)
    // 法線を持たないメッシュは長さ 0 で normalize が NaN になるため、下限を設けて向きなし (フェードで消える) として扱う
    float3 n = i.worldNormal * rsqrt(max(dot(i.worldNormal, i.worldNormal), 1e-8));
    float facing = dot(n, _DecalNormal.xyz);
    float fade = smoothstep(_FadeCosMin, _FadeCosMax, facing);

    // 板の Quad は Y180 回転で +X が root の -X になるため、U を反転して板と向きを揃える
    float2 uv = float2(0.5 - i.boxPos.x, i.boxPos.y + 0.5);
    fixed4 col = tex2D(_MainTex, uv) * _Color;
    col.rgb = PngApplySaturation(col.rgb, _Saturation);
    col.a *= fade;
    return col;
}

#endif
