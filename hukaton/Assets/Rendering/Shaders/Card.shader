Shader "Hakaton/Card"
{
    // Two-sided playing card: front art on the front face, deck back (mirrored) on the reverse.
    // The front texture's alpha defines the physical silhouette for both faces (torn edges, holes).
    Properties
    {
        _MainTex ("Front", 2D) = "white" {}
        _BackTex ("Back", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
        _FlashColor ("Flash colour", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0,1)) = 0
        _GlowColor ("Glow colour", Color) = (1,0.85,0.4,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _Alpha ("Alpha", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        ZTest [_ZTest]
        ZWrite [_ZWrite]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Card"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BackTex); SAMPLER(sampler_BackTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint, _FlashColor, _GlowColor;
                half _Flash, _Glow, _Cutoff, _Alpha;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 face = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(face.a - _Cutoff);
                half3 colour = front
                    ? face.rgb
                    : SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, float2(1.0 - input.uv.x, input.uv.y)).rgb;
                colour *= _Tint.rgb;
                // Soft glow: lift towards the glow colour, stronger near the silhouette edge.
                float2 centred = abs(input.uv - 0.5) * 2.0;
                half edge = saturate(max(centred.x, centred.y) * 1.25 - 0.35);
                colour = lerp(colour, colour + _GlowColor.rgb * (0.25 + edge * 0.6), _Glow);
                colour = lerp(colour, _FlashColor.rgb, _Flash);
                return half4(colour, _Alpha * _Tint.a);
            }
            ENDHLSL
        }
    }
}
