// Stylized river water for the tropical raft course (URP 17; needs Depth Texture + Opaque Texture, both enabled
// in PC_RPAsset).
//  * colour by real water depth (scene depth - surface depth): shallow turquoise -> deep teal
//  * refraction of the river bed through _CameraOpaqueTexture
//  * foam: depth intersection (rocks, banks, obstacles) + turbulence foam from vertex colour B, flowing
//  * procedural normals (no textures) scrolling along the river at the per-vertex flow speed (uv2.x)
//  * soft broad highlights + restrained fresnel reflection (no mirror look)
// Rendered in the transparent queue but composites the refraction itself, so it writes opaque colour
// (Blend One Zero): no sorting problems between water spans.
// Mesh data from RiverTerrainBuilder.BuildWater: uv = (lateral m, distance m) * 0.25, uv2 = (flow m/s, width),
// colour = (edge shallow, flow/10, turbulence, 1). The waterfall foam disc reuses it with _RADIAL_FLOW.
Shader "Env/StylizedWater"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.02, 0.42, 0.52, 1)
        _ShallowColor ("Shallow", Color) = (0.20, 0.85, 0.80, 1)
        _FoamColor ("Foam", Color) = (0.95, 1, 1, 1)
        _DepthFade ("Shallow -> Deep Distance (m)", Float) = 2.2
        _Clarity ("Clarity (refraction visibility)", Range(0, 1)) = 0.6
        _Refraction ("Refraction Strength", Range(0, 0.1)) = 0.025
        [Header(Flow)]
        _FlowScale ("Flow Speed Scale", Float) = 0.25
        _NormalScale ("Normal Strength", Range(0, 2)) = 0.3
        _WaveScale ("Wave Frequency", Float) = 0.45
        _WaveHeight ("Vertex Wave Height", Float) = 0.06
        [Header(Foam)]
        _FoamDistance ("Intersection Foam Distance (m)", Float) = 0.45
        _TurbulenceFoam ("Turbulence Foam", Range(0, 1.5)) = 0.6
        _EdgeFoam ("Bank Edge Foam", Range(0, 1)) = 0.12
        _SpotFoam ("Waterfall Foam Spots", Range(0, 1.5)) = 1
        [Header(Lighting)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.86
        _SpecularStrength ("Highlight Strength", Range(0, 2)) = 0.7
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.3
        [Toggle(_RADIAL_FLOW)] _RadialFlow ("Radial Flow (foam discs)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _RADIAL_FLOW
            // phones / web (set by PlatformBoot on the Mobile quality level): analytic waves, one depth sample, one foam noise
            #pragma multi_compile _ FO_MOBILE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _FoamColor;
                float _DepthFade, _Clarity, _Refraction;
                float _FlowScale, _NormalScale, _WaveScale, _WaveHeight;
                float _FoamDistance, _TurbulenceFoam, _EdgeFoam, _SpotFoam;
                half _Smoothness, _SpecularStrength, _ReflectionStrength;
            CBUFFER_END

            // waterfall impact foam: up to 16 spots (xyz = centre, w = radius), set globally by WaterFoamSpots
            float4 _FoamSpots[16];
            float _FoamSpotCount;

        #if defined(FO_MOBILE)
            // tiling value noise bound by FartOnline.Game.MobileNoise (8 lattice cells per tile; R = softWaves)
            TEXTURE2D(_FONoiseTex); SAMPLER(sampler_FONoiseTex);
        #endif

            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float vnoise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), u.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), u.x), u.y);
            }
            float waves(float2 p)
            {
                return vnoise2(p) * 0.5 + vnoise2(p * 2.1 + 7.3) * 0.3 + vnoise2(p * 4.3 + 1.7) * 0.2;
            }
            float softWaves(float2 p) { return vnoise2(p) * 0.65 + vnoise2(p * 1.9 + 4.1) * 0.35; }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float4 uvFlow : TEXCOORD3;         // xy uv, z flow speed
                half4 color : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Flow-map scrolling with bounded coordinates. The phase depends on TIME ONLY (a global cycle); the local
            // flow speed only scales how far each layer has scrolled. Two layers half a cycle apart are cross-faded
            // so the wrap is invisible. (Putting the per-vertex speed inside frac(time * speed) makes the phase
            // change hundreds of times along the river after a long session -> stripes across the water.)
            #define FLOW_CYCLE 3.0
            void FlowPhase(out float ph0, out float ph1, out float w)
            {
                float tt = _Time.y / FLOW_CYCLE;
                ph0 = frac(tt);
                ph1 = frac(tt + 0.5);
                w = abs(ph0 * 2.0 - 1.0);                               // 1 when layer 0 wraps -> use layer 1
            }

            float FlowWaves(float2 uv, float speed, float seed)
            {
                float ph0, ph1, w;
                FlowPhase(ph0, ph1, w);
                float dist = FLOW_CYCLE * speed * _FlowScale;           // uv scrolled per cycle at this speed
            #if defined(_RADIAL_FLOW)
                float r = length(uv);
                float a = atan2(uv.y, uv.x) / 6.2831853;
                float2 base = float2(a * 6.0, r * 1.5);
            #else
                float2 base = uv;
            #endif
                float n0 = waves(base - float2(0, ph0 * dist) + seed);
                float n1 = waves(base - float2(0, ph1 * dist) + seed + 13.7);
                return lerp(n0, n1, w);
            }

            // same scrolling, soft 2-octave noise (foam patches; no fine isolines)
            float FlowSoft(float2 uv, float speed, float seed)
            {
                float ph0, ph1, w;
                FlowPhase(ph0, ph1, w);
                float dist = FLOW_CYCLE * speed * _FlowScale;
                return lerp(softWaves(uv - float2(0, ph0 * dist) + seed), softWaves(uv - float2(0, ph1 * dist) + seed + 13.7), w);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = TransformObjectToWorld(input.positionOS.xyz);
                float turb = input.color.b;
                float speed = input.uv2.x;
                // gentle swell + choppier rapids; the shallow edges stay put so banks never show gaps
                float edge = input.color.r;
            #if defined(FO_MOBILE)
                // two travelling sine swells instead of 6 value-noise octaves per vertex
                float2 wpv = input.uv * _WaveScale;
                float wt = _Time.y * speed * _WaveScale * _FlowScale;
                float swell = sin(wpv.x * 1.9 + (wpv.y - wt) * 2.4) * 0.3 + sin(-wpv.x * 3.1 + (wpv.y - wt * 1.3) * 3.3 + 1.7) * 0.2;
                wp.y += swell * _WaveHeight * (1.0 + 3.0 * turb) * (1.0 - edge);
            #else
                wp.y += (FlowWaves(input.uv * _WaveScale, speed * _WaveScale, 0.0) - 0.5) * _WaveHeight * (1.0 + 3.0 * turb) * (1.0 - edge);
            #endif
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                VertexNormalInputs ni = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.normalWS = ni.normalWS;
                o.tangentWS = float4(ni.tangentWS, input.tangentOS.w);
                o.uvFlow = float4(input.uv, speed, 0);
                o.color = input.color;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }


            // mobile has no post-processing: the bright grading (saturation +30, contrast, exposure) is baked in here
            half3 MobileGrade(half3 c)
            {
                half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                c = lerp(l.xxx, c, 1.32h) * 1.12h;
                return max(0.0h, (c - 0.18h) * 1.1h + 0.18h);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float speed = i.uvFlow.z;
                float turb = i.color.b;
            #if defined(FO_MOBILE)
                {
                    // ---- mobile: analytic flowing ripples (two sine trains), depth from one sample, no refraction
                    float t = _Time.y;
                    float2 p = i.uvFlow.xy * _WaveScale;
                    float fl = speed * _WaveScale * _FlowScale;
                    float a1 = p.x * 1.7 + (p.y - t * fl * 0.9) * 2.3;
                    float a2 = -p.x * 2.9 + (p.y - t * fl * 1.3) * 3.1 + 1.3;
                    float2 g = float2(cos(a1) * 1.7 * 0.5 - cos(a2) * 2.9 * 0.35, cos(a1) * 2.3 * 0.5 + cos(a2) * 3.1 * 0.35);
                    g *= _NormalScale * (1.0 + turb * 1.5) * 0.12;
                    float3 N = SafeNormalize(i.normalWS);
                    float3 T = SafeNormalize(i.tangentWS.xyz);
                    float3 B = cross(N, T) * (i.tangentWS.w > 0 ? 1.0 : -1.0);
                    float3 n = SafeNormalize(N - T * g.x - B * g.y);
                    // no camera depth texture on mobile (saves a full-screen copy): depth from the bank-edge hint
                    float depth = (1.0 - i.color.r) * _DepthFade * 1.6 + 0.15;
                    float deep = max(saturate(depth / _DepthFade), (1.0 - i.color.r) * 0.35);
                    half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    half ndl = saturate(dot(n, L.direction) * 0.5 + 0.5);
                    col *= L.color * (0.55 + 0.45 * ndl) * lerp(0.55h, 1.0h, L.shadowAttenuation) + SampleSH(n) * 0.35;
                    float3 H = SafeNormalize(L.direction + V);
                    half spec = pow(saturate(dot(n, H)), exp2(10.0 * _Smoothness + 1.0)) * _SpecularStrength * L.shadowAttenuation;
                    half fres = pow(1.0 - saturate(dot(n, V)), 4.0);
                    col = lerp(col, GlossyEnvironmentReflection(reflect(-V, n), 1.0 - _Smoothness, 1.0), fres * _ReflectionStrength);
                    col += spec * L.color;
                    float2 fuv = i.uvFlow.xy * float2(1.4, 0.35);
                    float2 fnuv = (fuv - float2(0, t * speed * 0.42 * _FlowScale) + 6.34) * 0.125;
                    float fn = SAMPLE_TEXTURE2D(_FONoiseTex, sampler_FONoiseTex, fnuv).r;
                    float foam = smoothstep(0.55, 0.85, (1.0 - saturate(depth / _FoamDistance)) * (0.55 + fn * 0.6));
                    foam = max(foam, smoothstep(0.6, 0.88, fn + turb * 0.2) * smoothstep(0.2, 0.6, turb) * _TurbulenceFoam);
                    foam = max(foam, smoothstep(0.75, 1.0, i.color.r + fn * 0.25) * _EdgeFoam);
                    col = lerp(col, _FoamColor.rgb * (0.75 + 0.25 * ndl) * L.color, saturate(foam));
                    return half4(MixFog(MobileGrade(col), i.fogFactor), 1);
                }
            #endif
                // ---- procedural flowing normal (two layers at different phases)
                float2 uvA = i.uvFlow.xy * _WaveScale;
                float2 uvB = i.uvFlow.xy * 1.7 * _WaveScale;
                float sA = speed * _WaveScale, sB = speed * 1.7 * _WaveScale;
                const float e = 0.05;
                float hA = FlowWaves(uvA, sA, 0.0), hB = FlowWaves(uvB, sB, 3.17);
                float2 g = float2(FlowWaves(uvA + float2(e, 0), sA, 0.0) - hA, FlowWaves(uvA + float2(0, e), sA, 0.0) - hA) +
                           float2(FlowWaves(uvB + float2(e, 0), sB, 3.17) - hB, FlowWaves(uvB + float2(0, e), sB, 3.17) - hB) * 0.6;
                g *= _NormalScale * (1.0 + turb * 1.5) / e * 0.3;
                float3 N = SafeNormalize(i.normalWS);
                float3 T = SafeNormalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * (i.tangentWS.w > 0 ? 1.0 : -1.0);
                float3 n = SafeNormalize(N - T * g.x - B * g.y);

                // ---- depth below the surface
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float surfDepth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float2 ruv = suv + n.xz * _Refraction;
                float sceneRaw = SampleSceneDepth(ruv);
                float sceneDepth = LinearEyeDepth(sceneRaw, _ZBufferParams);
                if (sceneDepth < surfDepth) { ruv = suv; sceneDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams); }
                float depth = max(0.0, sceneDepth - surfDepth);
                float deep = saturate(depth / _DepthFade);
                deep = max(deep, (1.0 - i.color.r) * 0.35);                  // vertex hint: centre of the channel is deep

                half3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                half3 refr = SampleSceneColor(ruv);
                half clarity = _Clarity * (1.0 - deep);
                half3 col = lerp(waterCol, refr * _ShallowColor.rgb * 1.6, clarity * 0.6);

                // ---- lighting: soft diffuse + broad highlight + restrained reflection
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(0.55h, 1.0h, L.shadowAttenuation);
                half ndl = saturate(dot(n, L.direction) * 0.5 + 0.5);
                col *= L.color * (0.55 + 0.45 * ndl) * shadow + SampleSH(n) * 0.35;
                float3 H = SafeNormalize(L.direction + V);
                half specPow = exp2(10.0 * _Smoothness + 1.0);
                half spec = pow(saturate(dot(n, H)), specPow) * _SpecularStrength * L.shadowAttenuation;
                half fres = pow(1.0 - saturate(dot(n, V)), 4.0);
                half3 refl = GlossyEnvironmentReflection(reflect(-V, n), i.positionWS, 1.0 - _Smoothness, 1.0, i.screenPos.xy / i.screenPos.w);
                col = lerp(col, refl, fres * _ReflectionStrength);
                col += spec * L.color;

                // ---- foam
                // broad, low-frequency foam patterns (no speckle)
                // foam streaks are stretched along the flow (uv.y) like real whitewater
                float2 fuv = i.uvFlow.xy * float2(1.4, 0.35);
                float fn = FlowSoft(fuv, speed * 0.35 * 1.2, 6.34);
                float inter = 1.0 - saturate(depth / _FoamDistance);
                float foam = smoothstep(0.55, 0.85, inter * (0.55 + fn * 0.6));
                foam = max(foam, smoothstep(0.6, 0.88, fn + turb * 0.2) * smoothstep(0.2, 0.6, turb) * _TurbulenceFoam);
                foam = max(foam, smoothstep(0.75, 1.0, i.color.r + fn * 0.25) * _EdgeFoam);
                // waterfall impact spots: churning rings fading out with distance
                for (int k = 0; k < 16; k++)
                {
                    if (k >= (int)_FoamSpotCount) break;
                    float4 sp = _FoamSpots[k];
                    float2 dv = i.positionWS.xz - sp.xz;
                    float r = length(dv) / max(0.1, sp.w);
                    if (r > 1.0) continue;
                    float swirl = waves(float2(atan2(dv.y, dv.x) * 2.0, r * 3.0 - frac(_Time.y * 0.1) * 14.0) * 1.5);
                    foam = max(foam, smoothstep(0.25, 0.75, (1.0 - r) * (0.7 + swirl * 0.8)) * _SpotFoam);
                }
                col = lerp(col, _FoamColor.rgb * (0.75 + 0.25 * ndl) * L.color, saturate(foam));

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
