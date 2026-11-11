// Cartoon gas puffs for mesh particles (low-poly spheres). Two-tone toon lighting from the main light, soft
// transparent edges (view-facing falloff) so overlapping puffs read as one rounded cloud, and a depth fade
// where puffs touch the water or the deck. Colour = _BaseColor x particle colour (player accent / variant tint).
Shader "FartOnline/GasCloud"
{
    Properties
    {
        _BaseColor ("Lit Colour", Color) = (0.72, 0.92, 0.32, 1)
        _ShadeColor ("Shade Colour", Color) = (0.36, 0.62, 0.22, 1)
        _RimColor ("Rim Highlight", Color) = (1, 1, 0.75, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _EdgeSoftness ("Edge Softness", Range(0.1, 4)) = 1.2
        _ToonStep ("Toon Step", Range(0, 1)) = 0.45
        _DepthFade ("Depth Fade (m)", Range(0.01, 2)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "GasForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FO_MOBILE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _ShadeColor, _RimColor;
                half _Opacity, _EdgeSoftness, _ToonStep, _DepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight();
                half ndl = dot(n, l.direction) * 0.5h + 0.5h;
                half toon = smoothstep(_ToonStep - 0.08h, _ToonStep + 0.08h, ndl);
                half up = saturate(n.y * 0.5h + 0.6h);                       // puffy volume: undersides darker
                half3 col = lerp(_ShadeColor.rgb, _BaseColor.rgb, toon * up) * i.color.rgb;
                half ndv = saturate(dot(n, v));
                half rim = pow(1.0h - ndv, 3.0h) * toon;
                col += _RimColor.rgb * rim * 0.6h;
                col *= lerp(0.75h, 1.0h, l.color.r);
                half edge = saturate(pow(ndv, _EdgeSoftness) * 1.8h);
                // soft intersection with opaque geometry (water / deck / characters)
            #if defined(FO_MOBILE)
                half fade = 1.0h;                // mobile: no camera depth texture (saves a full-screen copy)
            #else
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float myZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half fade = saturate((sceneZ - myZ) / _DepthFade);
            #endif
                half a = i.color.a * _Opacity * edge * fade;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
