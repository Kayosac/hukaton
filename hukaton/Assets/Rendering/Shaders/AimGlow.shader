Shader "Hakaton/AimGlow"
{
    // Glowing line (the throw trajectory): the texture's red channel is how white a texel is — 1 in the thin core,
    // 0 in the halo — and its alpha is the glow falloff. The halo takes the vertex colour (the card's accent), the core
    // goes white and is lifted above 1 by Core Boost so the scene bloom (threshold 0.5) makes it shine. Alpha-blended,
    // so the line stays readable over a bright sky too.
    Properties
    {
        _MainTex ("Profile (R core, A glow)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _CoreBoost ("Core Boost", Range(1, 4)) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "AimGlow"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _CoreBoost;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Tint;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half core = texel.r;
                half3 colour = lerp(input.color.rgb, half3(1, 1, 1), core) * lerp(1, _CoreBoost, core);
                return half4(colour, texel.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
