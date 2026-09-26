Shader "SE/DecalOverlay"
{
    // SE/Decal のオーバーレイ版。Projector は受け側のメッシュごとに描くため、
    // 名前付き GrabPass で下地の取得を 1 フレーム 1 回に抑える。
    // そのためオーバーレイのデカール同士を重ねても互いは合成されない
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        _FadeCosMin ("Fade Cos Min", Float) = 0.1736
        _FadeCosMax ("Fade Cos Max", Float) = 0.5976
        // PngBlendMode の値
        _BlendMode ("Blend Mode", Float) = 3
        // SrcAlpha / OneMinusSrcAlpha
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        // 不透明物の後・半透明物の前に重ねる
        Tags
        {
            "Queue"="Transparent-500"
            "RenderType"="Transparent"
        }

        GrabPass { "_SEDecalOverlayGrab" }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Back
            // 受け側の面と同じ深度に描くため、面の傾きに応じて手前へずらし Z ファイティングを避ける。
            // 固定量 (units) は COM3D2.5 の深度バッファでは大きく効きすぎ、手前の物体を突き抜けて描かれるため使わない
            Offset -1, 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngCommon.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _Saturation;
            sampler2D _SEDecalOverlayGrab;
            // ワールド → 投影箱 (各軸 -0.5〜0.5)。Projector 組込みの行列は
            // エンジンのバージョンで名前が変わるため使わず、C# から渡す
            float4x4 _DecalMatrix;
            // 投影元の向き (root の +Z = 板の表側) のワールド方向
            float4 _DecalNormal;
            float _FadeCosMin;
            float _FadeCosMax;
            float _BlendMode;

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

            fixed4 frag (v2f i) : SV_Target
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
                fixed3 base = tex2Dproj(_SEDecalOverlayGrab, i.grabPos).rgb;
                return fixed4(PngOverlay(base, col.rgb), col.a);
            }
            ENDCG
        }
    }
}
