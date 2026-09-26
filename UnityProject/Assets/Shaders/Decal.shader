Shader "SE/Decal"
{
    // 既定値は PngDecalProjection の既定 (フェード角 80 度・通常ブレンド) 相当。実行時は C# が上書きする
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _FadeCosMin ("Fade Cos Min", Float) = 0.1736
        _FadeCosMax ("Fade Cos Max", Float) = 0.5976
        // PngDecalBlendMode の値
        _BlendMode ("Blend Mode", Float) = 0
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

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Back
            // 受け側の面と同じ深度に描くため手前へずらして Z ファイティングを避ける
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
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
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.boxPos = mul(_DecalMatrix, worldPos).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 箱の外 (奥行き方向の突き抜けを含む) は描かない。0.5 は PngDecalProjection の箱の半幅
                clip(0.5 - abs(i.boxPos));

                // 投影元を向く面ほど濃く、横向きの面は消す (床から壁の側面への伸びを抑える)
                float facing = dot(normalize(i.worldNormal), _DecalNormal.xyz);
                float fade = smoothstep(_FadeCosMin, _FadeCosMax, facing);

                // 板の Quad は Y180 回転で +X が root の -X になるため、U を反転して板と向きを揃える
                float2 uv = float2(0.5 - i.boxPos.x, i.boxPos.y + 0.5);
                fixed4 col = tex2D(_MainTex, uv) * _Color;
                col.a *= fade;

                if (_BlendMode > 0.5 && _BlendMode < 1.5)
                {
                    // _BlendMode 1 = PngDecalBlendMode.Multiply (DstColor Zero): 透明な所は白 (変化なし) へ寄せる
                    return fixed4(lerp(float3(1, 1, 1), col.rgb, col.a), 1);
                }
                return col;
            }
            ENDCG
        }
    }
}
