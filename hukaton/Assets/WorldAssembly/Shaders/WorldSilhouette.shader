// Far mountains and the start beacon seen through the fog: flat vertex colours, no fog, no lighting.
Shader "Hakaton/World Silhouette"
{
    Properties
    {
        _Color("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; half4 colour : COLOR; };
            struct Varyings { float4 position : SV_POSITION; half4 colour : COLOR; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.colour = input.colour;
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return input.colour * _Color; }
            ENDHLSL
        }
    }
}
