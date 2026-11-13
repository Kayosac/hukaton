Shader "Hakaton/Cards/Beacon through walls"
{
    // Merchant's nose: the chest pillars are seen through walls. Like Retro Arsenal's BasicGlow (texture × vertex
    // colour × tint × glow, alpha-blended), drawn twice: dimmed where something stands in front (ZTest Greater),
    // full where it stands in the open. Src/Dst SrcAlpha/One for an additive glow.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0, 15)) = 3
        _Hidden ("Behind walls", Range(0, 1)) = .5
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst (One: additive glow)", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Tint;
            half _Glow;
            half _Hidden;
        CBUFFER_END
        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.color = input.color;
            return output;
        }
        half4 Shade(Varyings input, half strength)
        {
            half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half3 colour = input.color.rgb * _Glow * _Tint.rgb * texel.rgb;
            return half4(colour, saturate(input.color.a * texel.a * _Tint.a * strength));
        }
        ENDHLSL

        Pass
        {
            Name "BehindWalls"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZTest Greater
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target { return Shade(input, _Hidden); }
            ENDHLSL
        }
        Pass
        {
            Name "InTheOpen"
            Tags { "LightMode"="UniversalForward" }
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target { return Shade(input, 1); }
            ENDHLSL
        }
    }
}
