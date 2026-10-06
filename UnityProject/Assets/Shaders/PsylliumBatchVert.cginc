#ifndef PSYLLIUM_BATCH_VERT_INCLUDED
#define PSYLLIUM_BATCH_VERT_INCLUDED

// PsylliumVert.cginc のバッチ描画版。バー 1 本ごとの位置をオブジェクト行列ではなく配列から読む

#include "UnityCG.cginc"

#define PSYLLIUM_BATCH_CAPACITY 1023

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _Color1a;
float4 _Color1b;
float4 _Color1c;
float4 _Color2a;
float4 _Color2b;
float4 _Color2c;
float _CutoffAlpha;

// バッチの親 (エリア) のローカル座標。_BarPos.w = 1 で有効、_BarUp.w = 色番号
float4 _BarPos[PSYLLIUM_BATCH_CAPACITY];
float4 _BarUp[PSYLLIUM_BATCH_CAPACITY];

struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1; // uv2.x = Radius, uv2.y = スロット番号
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1; // uv2.y = 色番号 (frag は旧シェーダーと同じ読み方)
};

v2f vert(appdata v)
{
    v2f o;
    int slot = (int)(v.uv2.y + 0.5);
    float4 barPosData = _BarPos[slot];
    float4 barUpData = _BarUp[slot];

    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.uv2 = float2(v.uv2.x, barUpData.w);

    if (barPosData.w < 0.5)
    {
        // 未使用スロットはクリップ範囲外へ飛ばして描かない
        o.pos = float4(2, 2, 2, 1);
        return o;
    }

    float3 barPos = mul(unity_ObjectToWorld, float4(barPosData.xyz, 1.0)).xyz;
    float3 barUp = mul((float3x3) unity_ObjectToWorld, barUpData.xyz);
    float3 cameraToBar = barPos - _WorldSpaceCameraPos;
    float3 barSide = normalize(cross(barUp, cameraToBar));
    float3 barForward = normalize(cross(barSide, barUp));

    // 旧シェーダーの「行列の列を差し替えて mul」と同じ合成
    float3 vertex = barSide * v.vertex.x + barUp * v.vertex.y + barForward * v.vertex.z + barPos;

    float3 offsetVec = normalize(cross(cameraToBar, barSide));
    vertex += offsetVec * v.uv2.x;

    o.pos = mul(UNITY_MATRIX_VP, float4(vertex, 1.0));
    return o;
}

#endif
