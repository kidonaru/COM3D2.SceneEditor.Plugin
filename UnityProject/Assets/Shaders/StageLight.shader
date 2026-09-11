Shader "MTE/StageLight"
{
    Properties
    {
        _MainTex ("Noise Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SubColor ("Sub Color", Color) = (1,1,1,1)
        _ScrollSpeed ("Scroll Speed", Vector) = (0.2,2.0,0,0)
        _FalloffExp ("Falloff Exponent", Range(0.1, 1)) = 0.5
        _EdgeSoftness ("Edge Softness", Range(0, 1)) = 1
        _SpotRange ("Spot Range", Float) = 10
        _OffsetRange ("Offset Range", Float) = 0.5
        _TanHalfAngle ("Tan Half Angle", Float) = 0.0875
        _CoreRadius ("Core Radius Ratio", Range(0, 1)) = 0.2
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.2
        _NoiseScaleInv ("Noise Scale Inverse", Range(0.1, 1)) = 0.2
        _Density ("Density", Float) = 0.1
        _DepthClip ("Depth Clip", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        // 裏面のみ描画: カメラが円錐内部にあっても描ける。遮蔽は深度テクスチャで行う
        Blend One One
        ZWrite Off
        Cull Front
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define SAMPLE_COUNT 16

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _ScrollSpeed;
            float4 _Color;
            float4 _SubColor;
            float _FalloffExp;
            float _EdgeSoftness;
            float _SpotRange;
            float _OffsetRange;
            float _TanHalfAngle;
            float _CoreRadius;
            float _NoiseStrength;
            float _NoiseScaleInv;
            float _Density;
            float _DepthClip;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            // 視線 (o + d*t) と無限円錐 x^2+y^2 = (k z)^2 の内部区間を求める。
            // 区間が無ければ false。二重円錐のもう一方は呼び出し側の z スラブで除外する
            bool IntersectCone(float3 o, float3 d, float k, float tzMin, float tzMax, out float t0, out float t1)
            {
                float k2 = k * k;
                float a = d.x * d.x + d.y * d.y - k2 * d.z * d.z;
                float b = 2.0 * (o.x * d.x + o.y * d.y - k2 * o.z * d.z);
                float c = o.x * o.x + o.y * o.y - k2 * o.z * o.z;

                t0 = tzMin;
                t1 = tzMax;

                if (abs(a) < 1e-6)
                {
                    // 視線が円錐の母線と平行: 一次式 b t + c < 0 が内部
                    if (abs(b) < 1e-6) return c < 0.0;
                    float t = -c / b;
                    if (b > 0.0) t1 = min(t1, t); else t0 = max(t0, t);
                    return t1 > t0;
                }

                float disc = b * b - 4.0 * a * c;
                if (disc < 0.0)
                {
                    // 実根なし: a<0 なら全域が内部、a>0 なら全域が外部
                    return a < 0.0;
                }

                float sq = sqrt(disc);
                float ra = (-b - sq) / (2.0 * a);
                float rb = (-b + sq) / (2.0 * a);
                float rMin = min(ra, rb);
                float rMax = max(ra, rb);

                if (a > 0.0)
                {
                    // 内部は根の間
                    t0 = max(t0, rMin);
                    t1 = min(t1, rMax);
                    return t1 > t0;
                }

                // a<0: 内部は根の外側 (2 区間)。z スラブと重なる方を採る
                float s0 = tzMin;
                float s1 = min(tzMax, rMin);
                if (s1 > s0)
                {
                    t0 = s0;
                    t1 = s1;
                    return true;
                }
                t0 = max(tzMin, rMax);
                t1 = tzMax;
                return t1 > t0;
            }

            float SampleNoise(float3 worldPos)
            {
                float2 uvXY = (worldPos.xy + _Time.x * _ScrollSpeed.xy) * _NoiseScaleInv;
                float2 uvZY = (worldPos.zy + _Time.x * _ScrollSpeed.xy) * _NoiseScaleInv;
                float n = (tex2D(_MainTex, frac(uvXY)).r + tex2D(_MainTex, frac(uvZY)).r) * 0.5;
                return 1.0 + (n - 0.5) * _NoiseStrength;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ワールド空間の視線。t はワールド距離
                float3 camWorld = _WorldSpaceCameraPos;
                float3 dirWorld = normalize(i.worldPos - camWorld);

                // オブジェクト空間へ変換。方向は正規化しない (t をワールド距離のまま扱うため)
                float3 o = mul(unity_WorldToObject, float4(camWorld, 1.0)).xyz;
                float3 d = mul((float3x3)unity_WorldToObject, dirWorld);

                // z スラブ [_OffsetRange, _SpotRange]
                float tzMin = 0.0;
                float tzMax = 1e10;
                if (abs(d.z) < 1e-6)
                {
                    if (o.z < _OffsetRange || o.z > _SpotRange) discard;
                }
                else
                {
                    float ta = (_OffsetRange - o.z) / d.z;
                    float tb = (_SpotRange - o.z) / d.z;
                    tzMin = max(0.0, min(ta, tb));
                    tzMax = max(ta, tb);
                }

                // 深度テクスチャで手前の不透明物にクリップ
                if (_DepthClip > 0.5)
                {
                    // 視線距離 t とビュー深度は比例する (このフラグメント自身の t と w で換算)
                    float sceneEye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    float fragT = length(i.worldPos - camWorld);
                    float fragEye = max(i.screenPos.w, 1e-4);
                    tzMax = min(tzMax, sceneEye * fragT / fragEye);
                }
                if (tzMax <= tzMin) discard;

                float t0, t1;
                if (!IntersectCone(o, d, _TanHalfAngle, tzMin, tzMax, t0, t1)) discard;

                // 区間内を等間隔サンプルして減衰とノイズを積分
                float step = (t1 - t0) / SAMPLE_COUNT;
                float sum = 0.0;
                // どちらも 0 になると smoothstep / 除算がゼロ割りになるため下限を設ける
                float edgeSoftness = max(_EdgeSoftness, 1e-4);
                float coreFalloffWidth = max(1.0 - _CoreRadius, 1e-4);
                for (int s = 0; s < SAMPLE_COUNT; s++)
                {
                    float t = t0 + step * (s + 0.5);
                    float3 p = o + d * t;
                    float z = max(p.z, 1e-4);

                    float zRate = saturate(z / _SpotRange);
                    float distanceFalloff = pow(1.0 - zRate, _FalloffExp);
                    distanceFalloff = smoothstep(0.0, edgeSoftness, distanceFalloff);

                    float normalizedRadius = length(p.xy) / max(z * _TanHalfAngle, 1e-6);
                    float rt = saturate((normalizedRadius - _CoreRadius) / coreFalloffWidth);
                    float angleFalloff = 1.0 - smoothstep(0.0, 1.0, rt);
                    angleFalloff = smoothstep(0.0, edgeSoftness, angleFalloff);

                    float3 pw = camWorld + dirWorld * t;
                    sum += distanceFalloff * angleFalloff * SampleNoise(pw);
                }

                // 積分値をそのまま飽和させると平坦なベタ塗りになり縁が硬く見えるため、
                // 1 に漸近する軟飽和で頭打ちを無くす
                float alpha = 1.0 - exp(-sum * step * _Density * _Color.a);
                float4 finalColor = lerp(_SubColor, _Color, alpha);
                finalColor.rgb *= alpha;
                finalColor.a = alpha;
                return finalColor;
            }
            ENDCG
        }
    }

    FallBack Off
}
