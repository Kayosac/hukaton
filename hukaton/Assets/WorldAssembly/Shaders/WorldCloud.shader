// The clouds over the endless world's rain fronts (WorldRain). Seen much further through the fog than the land is,
// so a front is seen coming: the scene's exp² fog applies to them only in part (_FogScale), and they go into the fog
// towards _Far, where they are no longer kept. Darker underneath, lighter on top; no other lighting.
Shader "Hakaton/World Cloud"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.47, 0.5, 0.56, 1)
        _Shade("Darker underneath", Range(0, 1)) = 0.45
        _FogDensity("Scene fog density (exp²)", Float) = 0.024
        _FogScale("Share of the scene's fog on the clouds", Range(0, 1)) = 0.3
        _Far("Gone at (m)", Float) = 210
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" }
        Pass
        {
            Name "Cloud"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Shade;
                float _FogDensity;
                float _FogScale;
                float _Far;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; float3 normal : NORMAL; };
            struct Varyings { float4 position : SV_POSITION; float3 world : TEXCOORD0; half up : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.position.xyz);
                output.position = TransformWorldToHClip(output.world);
                output.up = TransformObjectToWorldNormal(input.normal).y;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float d = distance(input.world, _WorldSpaceCameraPos);
                float k = _FogDensity * _FogScale * d;
                float seen = exp(-k * k) * (1 - smoothstep(_Far * 0.7, _Far, d));
                half light = lerp(1 - _Shade, 1 + _Shade * 0.35, saturate(input.up * 0.5 + 0.5));
                return half4(lerp(unity_FogColor.rgb, _BaseColor.rgb * light, seen), 1);
            }
            ENDHLSL
        }
    }
}
