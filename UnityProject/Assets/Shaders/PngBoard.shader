Shader "SE/PngBoard"
{
    // ゲーム組込みの CM3D2/Unlit_Texture_Photo_MyObject (マイオブジェクト) と同じ見た目の
    // 無照明・半透明の板に、ブレンド方式と彩度を足したもの。実行時の値は C# が上書きする
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        // PngBlendMode の値
        _BlendMode ("Blend Mode", Float) = 0
        // SrcAlpha / OneMinusSrcAlpha
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        // ゲーム組込みシェーダーと同じ名前。C# が透過画像なら 0 にする
        _ZWrite ("ZWrite", Float) = 1
        // UnityEngine.Rendering.CullMode。C# が両面描画 (0) にする
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
            fixed4 _Color;
            float _Saturation;
            float _BlendMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb = PngApplySaturation(col.rgb, _Saturation);
                return PngApplyMultiply(col, _BlendMode);
            }
            ENDCG
        }
    }
}
