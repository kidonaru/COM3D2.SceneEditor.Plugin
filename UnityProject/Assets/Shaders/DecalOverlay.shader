Shader "SE/DecalOverlay"
{
    // SE/Decal のオーバーレイ版。Projector は受け側のメッシュごとに描くため、
    // 名前付き GrabPass で下地の取得を 1 フレーム 1 回に抑える。
    // 取得は全カメラ通算で 1 回なので、先に描く SceneView カメラでは PngPlacementManager がこのデカールを止める。
    // オーバーレイのデカール同士を重ねても互いは合成されない
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
            // SE/Decal と同じ理由で、面の傾きに応じて手前へずらす
            Offset -1, 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PngDecalCommon.cginc"

            sampler2D _SEDecalOverlayGrab;

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = PngDecalColor(i);
                fixed3 base = tex2Dproj(_SEDecalOverlayGrab, i.grabPos).rgb;
                // 不透明度は通常の半透明合成 (SrcAlpha / OneMinusSrcAlpha) で効かせる
                return fixed4(PngOverlay(base, col.rgb), col.a);
            }
            ENDCG
        }
    }
}
