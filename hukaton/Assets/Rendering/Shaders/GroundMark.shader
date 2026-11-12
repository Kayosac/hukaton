Shader "Hakaton/Ground Mark"
{
    // A mark drawn on the ground (a card's reach): Hakaton/Particle (texture × vertex colour, black ink stays black)
    // with every vertex pulled toward the camera by _DepthPull metres, so a stroke lying on the ground is not eaten
    // by the ground's own (low-resolution) depth, yet still hides behind real hills and walls.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _DepthPull ("Pull toward the camera, m", Float) = 0.35
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            Name "GroundMark"
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
                float _DepthPull;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 toCamera = GetCameraPositionWS() - positionWS;
                float distance = length(toCamera);
                // Never past the camera's near side of the mark.
                positionWS += toCamera / max(distance, 1e-4) * min(_DepthPull, distance * 0.5);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Tint;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half fill = saturate(max(texel.r, max(texel.g, texel.b)) * 1.4);
                half3 colour = lerp(texel.rgb, texel.rgb * input.color.rgb, fill);
                return half4(colour, texel.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
