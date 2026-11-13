// Far mountains, the low ground under them and the start beacon seen through the fog. Each vertex carries the colour
// it would have in clear air; the shader fogs it the way the scene's exp² fog would. Raised ground (vertex alpha 1)
// also comes back out of the fog as a silhouette: nothing at _Near (where the real chunks stop), full by
// _Near + _FadeIn, and back into the fog towards _Far. No lighting.
Shader "Hakaton/World Silhouette"
{
    Properties
    {
        _Near("Fade in from (m)", Float) = 64
        _FadeIn("Fade in over (m)", Float) = 40
        _Far("Gone at (m)", Float) = 200
        _FogDensity("Scene fog density (exp²)", Float) = 0.024
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
                float _FogDensity;
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
                float d = distance(input.world, _WorldSpaceCameraPos);
                // What the scene's own fog leaves of anything this far: low ground right past the loaded chunks
                // looks just like the loaded ground next to it.
                float k = _FogDensity * d;
                float seen = exp(-k * k);
                // Raised ground (alpha 1) also comes out of the fog with distance, as a silhouette.
                float silhouette = input.colour.a * smoothstep(_Near, _Near + max(_FadeIn, 1), d) * (1 - smoothstep(_Far * .7, _Far, d));
                return half4(lerp(unity_FogColor.rgb, input.colour.rgb, max(seen, silhouette)), 1);
            }
            ENDHLSL
        }

        // Depth for the presentation camera's native-resolution occlusion (NativeWorldUIPass draws only DepthOnly).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Near;
                float _FadeIn;
                float _Far;
                float _FogDensity;
            CBUFFER_END

            float4 vert(float4 position : POSITION) : SV_POSITION { return TransformObjectToHClip(position.xyz); }
            half frag(float4 position : SV_POSITION) : SV_Target { return position.z; }
            ENDHLSL
        }
    }
}
