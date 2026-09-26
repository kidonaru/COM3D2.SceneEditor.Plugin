Shader "SE/PngBoardOverlay"
{
    // SE/PngBoard のオーバーレイ版。板ごとに下地を取得して合成する
    // (板は 1 枚 1 描画なので、名前なし GrabPass でも取得回数は板の枚数で済む)
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        _BlendMode ("Blend Mode", Float) = 3
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        _ZWrite ("ZWrite", Float) = 1
        _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        GrabPass { }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _GrabTexture;
            fixed4 _Color;
            float _Saturation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                fixed3 base = tex2Dproj(_GrabTexture, i.grabPos).rgb;
                // 不透明度は通常の半透明合成 (SrcAlpha / OneMinusSrcAlpha) で効かせる
                return fixed4(PngOverlay(base, col.rgb), col.a);
            }
            ENDCG
        }
    }
}
