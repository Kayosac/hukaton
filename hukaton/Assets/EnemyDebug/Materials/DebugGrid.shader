Shader "Hakaton/Debug/Enemy Grid"
{
    Properties { _BaseColor("Floor", Color) = (0.16,0.20,0.23,1) _LineColor("Grid", Color) = (0.27,0.34,0.37,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _LineColor;
            CBUFFER_END
            V vert(A i) { V o; o.world = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.world); return o; }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.world.xz;
                float2 d = abs(frac(uv + .5) - .5) / max(fwidth(uv), .001);
                float gridLine = 1 - saturate(min(d.x,d.y));
                float2 major = abs(frac(uv / 10 + .5) - .5) / max(fwidth(uv / 10), .001);
                float mainLine = 1 - saturate(min(major.x, major.y) * .5);
                float fade = saturate(1 - distance(i.world, _WorldSpaceCameraPos) / 70);
                return lerp(_BaseColor, _LineColor, max(gridLine * .35, mainLine) * fade);
            }
            ENDHLSL
        }
    }
}
