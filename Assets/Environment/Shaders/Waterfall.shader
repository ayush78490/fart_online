// Stylized waterfall sheet (URP 17). Translucent, streaks flowing down (uv.y grows downwards), brighter
// churned water at the top lip and the bottom, alpha from vertex alpha (soft side edges).
// Mesh: uv.x across 0..1, uv.y = metres from the top * 0.3, vertex colour a = edge fade, r = bottom foam weight.
Shader "Env/Waterfall"
{
    Properties
    {
        _Color ("Water", Color) = (0.55, 0.92, 0.95, 1)
        _DeepColor ("Streak Shadow", Color) = (0.10, 0.55, 0.62, 1)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _FlowSpeed ("Flow Speed", Float) = 1.6
        _StreakScale ("Streak Scale", Float) = 9
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _FoamTop ("Lip Foam", Range(0, 1)) = 0.5
        _FoamBottom ("Bottom Foam", Range(0, 1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Waterfall"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _DeepColor, _FoamColor;
                float _FlowSpeed, _StreakScale;
                half _Opacity, _FoamTop, _FoamBottom;
            CBUFFER_END

            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float vnoise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), u.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), u.x), u.y);
            }

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2; half4 color : TEXCOORD3; half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            V Vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.color = i.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                // bounded scrolling (two wrapped layers cross-faded) keeps noise coordinates small forever
                const float P = 8.0;
                float tt = _Time.y * _FlowSpeed / P;
                float ph0 = frac(tt), ph1 = frac(tt + 0.5), w = abs(ph0 * 2.0 - 1.0);
                float t = ph0 * P, t1 = ph1 * P;
                // vertical streaks: stretched noise scrolling downwards, two layers
                float s1 = lerp(vnoise2(float2(i.uv.x * _StreakScale, i.uv.y * 0.7 - t)),
                                vnoise2(float2(i.uv.x * _StreakScale + 3.3, i.uv.y * 0.7 - t1)), w);
                float s2 = lerp(vnoise2(float2(i.uv.x * _StreakScale * 2.3 + 5.1, i.uv.y * 1.3 - t * 1.4)),
                                vnoise2(float2(i.uv.x * _StreakScale * 2.3 + 9.4, i.uv.y * 1.3 - t1 * 1.4)), w);
                float streak = s1 * 0.6 + s2 * 0.4;
                half3 col = lerp(_DeepColor.rgb, _Color.rgb, smoothstep(0.25, 0.8, streak));
                // foam at the lip and the bottom (vertex colour r), broken up by noise
                float churn = lerp(vnoise2(float2(i.uv.x * 14.0, i.uv.y * 3.0 - t * 2.0)), vnoise2(float2(i.uv.x * 14.0 + 2.1, i.uv.y * 3.0 - t1 * 2.0)), w);
                float foam = saturate(_FoamTop * (1.0 - saturate(i.uv.y * 2.5)) + _FoamBottom * i.color.r) * smoothstep(0.3, 0.7, churn + 0.25);
                foam = max(foam, smoothstep(0.78, 0.95, streak) * 0.6);
                col = lerp(col, _FoamColor.rgb, foam);
                // degenerate edge triangles have zero normals: normalize() would return NaN (rendered black)
                float3 n = SafeNormalize(i.normalWS) * (front ? 1.0 : -1.0);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit = 0.6 + 0.4 * saturate(dot(n, L.direction) * 0.5 + 0.5);
                col *= L.color * lit * lerp(0.7h, 1.0h, L.shadowAttenuation) + SampleSH(n) * 0.25;
                half alpha = _Opacity * i.color.a * saturate(0.55 + streak * 0.6 + foam);
                col = MixFog(col, i.fog);
                return half4(max(col, 0.0h), saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
