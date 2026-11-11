// Stylized environment surface for the tropical raft course (URP 17).
// Modes (material keyword):
//   _MODE_ROCK    canyon walls + rocks: warm strata bands (world height + noise) mixed with vertex colour R,
//                 wet/dark near water (vertex colour G), grass on upward faces where vertex colour B allows,
//                 procedural micro detail + bump. No textures, so generated chunks always join seamlessly.
//   _MODE_FOLIAGE leaves/grass: base x vertex colour, wind sway weighted by vertex alpha, soft translucency,
//                 lit on both sides.
//   _MODE_WOOD    planks/beams: restrained procedural grain along the object's local Y axis.
Shader "Env/StylizedSurface"
{
    Properties
    {
        [KeywordEnum(Rock, Foliage, Wood)] _Mode ("Mode", Float) = 0
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Header(Rock palette)]
        _ColorA ("Strata A", Color) = (0.72, 0.33, 0.20, 1)
        _ColorB ("Strata B", Color) = (0.86, 0.52, 0.28, 1)
        _ColorDark ("Crevice", Color) = (0.38, 0.24, 0.18, 1)
        _ColorWet ("Wet", Color) = (0.30, 0.20, 0.16, 1)
        _GrassColor ("Grass", Color) = (0.36, 0.66, 0.20, 1)
        _StrataScale ("Strata Frequency", Float) = 0.55
        _DarkAmount ("Crevice Amount", Range(0, 1)) = 0.45
        _GrassAmount ("Grass Amount", Range(0, 1)) = 1
        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.18
        _WetSmoothness ("Wet Smoothness", Range(0, 1)) = 0.62
        _DetailScale ("Detail Scale", Float) = 0.9
        _DetailStrength ("Detail Albedo", Range(0, 0.5)) = 0.12
        _BumpStrength ("Detail Bump", Range(0, 2)) = 0.45
        [Header(Foliage)]
        _Translucency ("Translucency", Range(0, 1)) = 0.35
        _WindStrength ("Wind Strength", Float) = 0.08
        _WindSpeed ("Wind Speed", Float) = 1.6
        [Header(Wood)]
        _GrainScale ("Grain Scale", Float) = 6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _ColorA, _ColorB, _ColorDark, _ColorWet, _GrassColor;
            float _StrataScale, _DarkAmount, _GrassAmount;
            half _Smoothness, _WetSmoothness;
            float _DetailScale, _DetailStrength, _BumpStrength;
            half _Translucency;
            float _WindStrength, _WindSpeed, _GrainScale;
        CBUFFER_END

        // ---- procedural value noise (deterministic, texture-free)
        float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
        float vnoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);
            float n000 = hash13(i), n100 = hash13(i + float3(1,0,0)), n010 = hash13(i + float3(0,1,0)), n110 = hash13(i + float3(1,1,0));
            float n001 = hash13(i + float3(0,0,1)), n101 = hash13(i + float3(1,0,1)), n011 = hash13(i + float3(0,1,1)), n111 = hash13(i + float3(1,1,1));
            return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y), lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
        }
        float fbm3(float3 p) { return vnoise(p) * 0.55 + vnoise(p * 2.03 + 17.1) * 0.3 + vnoise(p * 4.1 + 3.7) * 0.15; }

        float3 WindOffset(float3 positionWS, float weight)
        {
            float t = _Time.y * _WindSpeed;
            float sway = sin(t + positionWS.x * 0.35 + positionWS.z * 0.27) + 0.35 * sin(t * 2.3 + positionWS.y * 1.7);
            return float3(0.8, 0.0, 0.5) * sway * _WindStrength * weight;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _MODE_ROCK _MODE_FOLIAGE _MODE_WOOD
            // phones / web (set by PlatformBoot on the Mobile quality level): noise per vertex, no per-pixel fbm or bump
            #pragma multi_compile _ FO_MOBILE

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #if defined(FO_MOBILE)
            // tiling value noise bound by FartOnline.Game.MobileNoise (8 lattice cells per tile;
            // G = fbm3, B = vnoise). Two skewed planar projections stand in for 3D noise.
            TEXTURE2D(_FONoiseTex); SAMPLER(sampler_FONoiseTex);
            half4 FONoise3(float3 p)
            {
                p *= 0.125;
                float2 uv1 = float2(p.x + p.y * 0.6, p.z - p.y * 0.35);
                float2 uv2 = float2(p.y - p.x * 0.5, p.z * 0.7 + p.x * 0.6 + p.y * 0.3) + 0.37;
                half4 n = (SAMPLE_TEXTURE2D_LOD(_FONoiseTex, sampler_FONoiseTex, uv1, 0) + SAMPLE_TEXTURE2D_LOD(_FONoiseTex, sampler_FONoiseTex, uv2, 0)) * 0.5h;
                return saturate((n - 0.5h) * 1.41h + 0.5h);           // averaging two fields halves the contrast
            }
        #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            #if defined(FO_MOBILE)
                half4 noiseV : TEXCOORD6;          // rock: large noise, crevices, grass mask, grass tint (per vertex)
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD9;
            #endif
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            #if defined(_MODE_FOLIAGE)
                positionWS += WindOffset(positionWS, input.color.a);
            #endif
                o.positionWS = positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
            #if defined(FO_MOBILE) && defined(_MODE_ROCK)
                o.noiseV = half4(FONoise3(positionWS * 0.06).g, FONoise3(positionWS * 0.16 + 5.3).g, FONoise3(positionWS * 0.22 + 11.0).g, FONoise3(positionWS * 0.7).b);
            #endif
                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, o.staticLightmapUV);
            #ifdef DYNAMICLIGHTMAP_ON
                o.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #endif
                OUTPUT_SH4(positionWS, o.normalWS.xyz, GetWorldSpaceNormalizeViewDir(positionWS), o.vertexSH, o.probeOcclusion);
                return o;
            }


            // mobile has no post-processing: the bright grading (saturation +30, contrast, exposure) is baked in here
            half3 MobileGrade(half3 c)
            {
                half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                c = lerp(l.xxx, c, 1.32h) * 1.12h;
                return max(0.0h, (c - 0.18h) * 1.1h + 0.18h);
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 wp = input.positionWS;
                float3 n = SafeNormalize(input.normalWS);
                if (!frontFace) n = -n;
                half3 albedo;
                half smooth = _Smoothness;

            #if defined(_MODE_ROCK) && defined(FO_MOBILE)
                // mobile: the same look from per-vertex noise, one cheap detail octave, no bump
                float bands = sin(wp.y * _StrataScale + input.noiseV.x * 5.0) * 0.5 + 0.5;
                float strata = saturate(lerp(bands, input.color.r, 0.45));
                albedo = lerp(_ColorA.rgb, _ColorB.rgb, strata);
                albedo = lerp(albedo, _ColorDark.rgb, saturate((input.noiseV.y - 0.5) * 3.0) * _DarkAmount);
                float wet = saturate(input.color.g);
                albedo = lerp(albedo, _ColorWet.rgb, wet * 0.8);
                smooth = lerp(_Smoothness, _WetSmoothness, wet);
                float grass = saturate(input.color.b * _GrassAmount) * smoothstep(0.55, 0.8, n.y) * (1.0 - wet);
                grass *= smoothstep(0.35, 0.55, input.noiseV.z + input.color.b * 0.3);
                albedo = lerp(albedo, _GrassColor.rgb * (0.85 + 0.3 * input.noiseV.w), grass);
                smooth = lerp(smooth, 0.08, grass);
                float2 duv = float2(wp.x + wp.y * 0.6, wp.z - wp.y * 0.35) * (_DetailScale * 0.125);
                albedo *= 1.0 + (SAMPLE_TEXTURE2D(_FONoiseTex, sampler_FONoiseTex, duv).b - 0.5) * 2.0 * _DetailStrength;
                albedo *= _BaseColor.rgb;
            #elif defined(_MODE_ROCK)
                float big = fbm3(wp * 0.06);
                float bands = sin(wp.y * _StrataScale + big * 5.0) * 0.5 + 0.5;
                float strata = saturate(lerp(bands, input.color.r, 0.45));
                albedo = lerp(_ColorA.rgb, _ColorB.rgb, strata);
                float crev = saturate((fbm3(wp * 0.16 + 5.3) - 0.5) * 3.0) * _DarkAmount;
                albedo = lerp(albedo, _ColorDark.rgb, crev);
                float wet = saturate(input.color.g);
                albedo = lerp(albedo, _ColorWet.rgb, wet * 0.8);
                smooth = lerp(_Smoothness, _WetSmoothness, wet);
                float grass = saturate(input.color.b * _GrassAmount) * smoothstep(0.55, 0.8, n.y) * (1.0 - wet);
                grass *= smoothstep(0.35, 0.55, fbm3(wp * 0.22 + 11.0) + input.color.b * 0.3);
                albedo = lerp(albedo, _GrassColor.rgb * (0.85 + 0.3 * vnoise(wp * 0.7)), grass);
                smooth = lerp(smooth, 0.08, grass);
                // micro detail + bump. The bump uses screen-space derivatives of the detail height (surface-gradient
                // method) instead of three extra noise evaluations per pixel: ~40% cheaper.
                float3 dp = wp * _DetailScale;
                float d0 = fbm3(dp);
                albedo *= 1.0 + (d0 - 0.5) * 2.0 * _DetailStrength;
                float3 dpdx = ddx(wp), dpdy = ddy(wp);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 surfGrad = sign(det) * (ddx(d0) * r1 + ddy(d0) * r2);
                n = SafeNormalize(abs(det) * n - surfGrad * _BumpStrength * 0.6);   // can be ~0 at grazing silhouettes: never NaN
                albedo *= _BaseColor.rgb;
            #elif defined(_MODE_FOLIAGE) && defined(FO_MOBILE)
                albedo = _BaseColor.rgb * input.color.rgb;
            #elif defined(_MODE_FOLIAGE)
                albedo = _BaseColor.rgb * input.color.rgb * (0.9 + 0.2 * vnoise(wp * 1.3));
            #elif defined(FO_MOBILE)
                float3 op = input.positionOS * float3(1.0, 0.12, 1.0);
                float grain = sin((op.x + op.z) * _GrainScale + sin(op.y * 7.0 + op.x * 3.0) * 2.0) * 0.5 + 0.5;
                albedo = _BaseColor.rgb * input.color.rgb * (0.86 + 0.14 * grain);
            #else
                float3 op = input.positionOS * float3(1.0, 0.12, 1.0);
                float grain = sin((op.x + op.z) * _GrainScale + fbm3(op * 3.0) * 6.0) * 0.5 + 0.5;
                albedo = _BaseColor.rgb * input.color.rgb * (0.86 + 0.14 * grain) * (0.93 + 0.14 * vnoise(wp * 2.0));
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = wp;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(wp);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(wp);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(wp, 1.0), input.fogFactor);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, n);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(wp), n, inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, n);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif

            #if defined(FO_MOBILE)
                {
                    // mobile: half-lambert sun with shadows + ambient SH + a soft wet sheen, instead of full PBR
                    Light ml = GetMainLight(inputData.shadowCoord);
                    half ndl = saturate(dot(n, ml.direction) * 0.8h + 0.2h);
                    half3 lit = albedo * (ml.color * ndl * ml.shadowAttenuation + inputData.bakedGI);
                    half3 h = SafeNormalize(ml.direction + inputData.viewDirectionWS);
                    lit += ml.color * pow(saturate(dot(n, h)), 24.0h) * smooth * 0.35h * ml.shadowAttenuation;
                    return half4(MixFog(MobileGrade(lit), inputData.fogCoord), 1);
                }
            #endif
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = smooth;
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1;
                half4 color = UniversalFragmentPBR(inputData, s);
            #if defined(_MODE_FOLIAGE)
                Light mainLight = GetMainLight(inputData.shadowCoord, wp, inputData.shadowMask);
                half back = pow(saturate(dot(-mainLight.direction, inputData.viewDirectionWS)), 4.0) + 0.25 * saturate(-dot(n, mainLight.direction));
                color.rgb += albedo * mainLight.color * back * _Translucency * mainLight.shadowAttenuation;
            #endif
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _MODE_ROCK _MODE_FOLIAGE _MODE_WOOD
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V ShadowVert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
            #if defined(_MODE_FOLIAGE)
                wp += WindOffset(wp, i.color.a);
            #endif
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - wp);
            #else
                float3 ld = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(wp, nw, ld)));
                return o;
            }
            half4 ShadowFrag(V i) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DVert
            #pragma fragment DFrag
            #pragma shader_feature_local _MODE_ROCK _MODE_FOLIAGE _MODE_WOOD
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V DVert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
            #if defined(_MODE_FOLIAGE)
                wp += WindOffset(wp, i.color.a);
            #endif
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }
            half DFrag(V i) : SV_TARGET { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NVert
            #pragma fragment NFrag
            #pragma shader_feature_local _MODE_ROCK _MODE_FOLIAGE _MODE_WOOD
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V NVert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
            #if defined(_MODE_FOLIAGE)
                wp += WindOffset(wp, i.color.a);
            #endif
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 NFrag(V i) : SV_Target0 { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
