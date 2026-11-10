// Additive light shaft for the tutorial cave skylights (water hall and grotto).
// Unlit, no depth write, both sides; fades out close to the camera so walking through it never flashes.
Shader "Hukaton/Cave Godray"
{
    Properties
    {
        _BaseMap ("Streaks (alpha = strength)", 2D) = "white" {}
        _Color ("Colour", Color) = (0.55, 0.75, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.2
        _Scroll ("Streak drift", Float) = 0.015
        _NearFade ("Fade near camera (m)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                half _Intensity;
                float _Scroll;
                float _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert (Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.uv.x += _Time.y * _Scroll;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half nearFade = saturate(distance(i.positionWS, _WorldSpaceCameraPos) / max(_NearFade, 0.01));
                half3 c = _Color.rgb * t.rgb * t.a * _Intensity * nearFade;
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
