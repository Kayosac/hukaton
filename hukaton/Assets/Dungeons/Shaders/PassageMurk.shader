// The depth of a dead-end passage (the way between the surface and a dungeon): darkness or daylight that grows with
// every metre into it, laid on whatever surface the eye meets there. A box round the passage is drawn from inside
// (back faces, depth test off); each pixel finds the scene surface behind it from the depth texture, places it in the
// passage (the box's own frame: +Z inwards) and takes a share by how deep in it lies. Walls, floor and the dead end
// darken (or glow) smoothly into the distance, nothing is a flat cut-out. Surfaces outside the passage (a room's wall
// in front of it) are left alone.
// Dark: alpha blended black (_SrcBlend SrcAlpha, _DstBlend OneMinusSrcAlpha, _Additive 0).
// Glow: added light (_SrcBlend One, _DstBlend One, _Additive 1).
Shader "Hakaton/Passage Murk"
{
    Properties
    {
        _Colour("Colour", Color) = (0, 0, 0, 1)
        _From("Starts this deep (m)", Float) = 0.5
        _To("Full this deep (m)", Float) = 7.5
        _Power("Curve", Float) = 1.4
        _Strength("Strength", Float) = 1
        _Haze("Air (share of the depth seen through)", Float) = 0.35
        _Box("Half width, height, depth (m)", Vector) = (2.3, 6.6, 8, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 10
        _Additive("Additive", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "PassageMurk"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
                float _From;
                float _To;
                float _Power;
                float _Strength;
                float _Haze;
                float4 _Box;
                float _SrcBlend;
                float _DstBlend;
                float _Additive;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.world = TransformObjectToWorld(input.position.xyz);
                o.position = TransformWorldToHClip(o.world);
                o.screen = ComputeScreenPos(o.position);
                return o;
            }

            float Depth(float z) { return pow(saturate((z - _From) / max(_To - _From, 0.01)), _Power); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = i.world - cam;
                // Eye depths along the same ray: the box's far side and the scene surface. The nearer one is what is seen.
                float face = max(-TransformWorldToView(i.world).z, 1e-4);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 seen = cam + ray * (min(scene, face) / face);
                float3 p = TransformWorldToObject(seen);
                float3 c = TransformWorldToObject(cam);
                // Only what lies in the passage (a room's wall in front of it stays as it is).
                float inside = step(abs(p.x), _Box.x + 0.6) * step(p.y, _Box.y + 0.6) * step(-1.0, p.y) * step(-0.6, p.z) * step(p.z, _Box.z + 0.8);
                // The surface's own depth, plus the air in between: how much of the deep part the eye looks through.
                float surface = Depth(p.z);
                float air = saturate((Depth(p.z) - Depth(max(c.z, 0))) ) * _Haze;
                float k = saturate((surface + air) * _Strength) * inside;
                if (_Additive > 0.5) return half4(_Colour.rgb * k, 1);
                return half4(_Colour.rgb, k);
            }
            ENDHLSL
        }
    }
}
