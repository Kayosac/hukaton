Shader "Hidden/Hukaton/World Presentation"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Present fixed-resolution world"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_WorldImage);
            CBUFFER_START(UnityPerMaterial)
                float4 _WorldSize, _OutputRect, _BorderColor;
            CBUFFER_END
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = (input.texcoord - _OutputRect.xy) / _OutputRect.zw;
                if (any(uv < 0) || any(uv >= 1)) return float4(_BorderColor.rgb, 1);
                // Explicit texel centers prevent filtering and retain the already-rendered dither pattern.
                uv = (floor(uv * _WorldSize.xy) + .5) / _WorldSize.xy;
                return float4(SAMPLE_TEXTURE2D(_WorldImage, sampler_PointClamp, uv).rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "World UI occlusion depth"
            ColorMask 0
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings DepthVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz); return output;
            }
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
