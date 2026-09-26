Shader "SE/Decal"
{
    // 既定値は PngDecalProjection の既定 (フェード角 80 度・通常ブレンド) 相当。実行時は C# が上書きする
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        _FadeCosMin ("Fade Cos Min", Float) = 0.1736
        _FadeCosMax ("Fade Cos Max", Float) = 0.5976
        // PngBlendMode の値
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
            // 受け側の面と同じ深度に描くため、面の傾きに応じて手前へずらし Z ファイティングを避ける。
            // 固定量 (units) は COM3D2.5 の深度バッファでは大きく効きすぎ、手前の物体を突き抜けて描かれるため使わない
            Offset -1, 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngDecalCommon.cginc"

            float _BlendMode;

            fixed4 frag (v2f i) : SV_Target
            {
                return PngApplyBlendOutput(PngDecalColor(i), _BlendMode);
            }
            ENDCG
        }
    }
}
