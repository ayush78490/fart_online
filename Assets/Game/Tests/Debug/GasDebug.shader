Shader "FartOnline/Debug/GasDebug"
{
    Properties { _Mode ("0 color 1 normal", Float) = 0 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Mode;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; half4 color : COLOR; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.n = i.normalOS; o.color = i.color; return o; }
            half4 frag(V i) : SV_Target { return _Mode < 0.5 ? half4(i.color.rgb, 1) : half4(i.n * 0.5 + 0.5, 1); }
            ENDHLSL
        }
    }
}
