// Far mountains and the start beacon seen through the fog. Each vertex carries the colour it would have in clear air;
// the shader blends it with the scene's fog colour by distance: nothing at _Near (where the real chunks stop), full
// by _Near + _FadeIn, and back into the fog towards _Far. No lighting, no scene fog.
Shader "Hakaton/World Silhouette"
{
    Properties
    {
        _Near("Fade in from (m)", Float) = 64
        _FadeIn("Fade in over (m)", Float) = 40
        _Far("Gone at (m)", Float) = 200
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
                float _Near;
                float _FadeIn;
                float _Far;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; half4 colour : COLOR; };
            struct Varyings { float4 position : SV_POSITION; half4 colour : COLOR; float3 world : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.position.xyz);
                output.position = TransformWorldToHClip(output.world);
                output.colour = input.colour;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float d = distance(input.world.xz, _WorldSpaceCameraPos.xz);
                float v = smoothstep(_Near, _Near + max(_FadeIn, 1), d) * (1 - smoothstep(_Far * .7, _Far, d));
                return half4(lerp(unity_FogColor.rgb, input.colour.rgb, v), 1);
            }
            ENDHLSL
        }
    }
}
