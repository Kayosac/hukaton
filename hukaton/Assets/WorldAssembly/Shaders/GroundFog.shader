// A bank of ground fog over the rotten swamp (SwampLife): a few stacked discs low over the water, thick in the middle,
// gone at the rim (vertex alpha), torn by slow drifting noise, denser low down (UV1.x: the layer's height share). Soft
// where it meets the ground and the trunks (scene depth), and thinned right at the camera, so walking in it does not
// show the layers; the white world inside a bank is the scene fog's work (WorldAtmosphere "mist").
// Cheap by design: no raymarching, one depth read and two noise octaves a pixel. _Fade: the bank coming and going.
Shader "Hakaton/Ground Fog"
{
    Properties
    {
        _BaseColor("Colour (mixed with the scene fog's)", Color) = (0.8, 0.84, 0.8, 1)
        _FogShare("Share of the scene fog's colour", Range(0, 1)) = 0.55
        _Density("Density", Range(0, 2)) = 0.85
        _Scale("Noise scale (1/m)", Float) = 0.11
        _Drift("Noise drift (m/s)", Vector) = (0.25, 0.08, 0, 0)
        _Soft("Soft against the ground (m)", Float) = 1.1
        _Near("Thinned within (m) of the camera", Float) = 2.5
        _Fade("Fade", Range(0, 1)) = 1
        _Seed("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }
        Pass
        {
            Name "GroundFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/WorldAssembly/Shaders/WorldGrade.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _FogShare;
                half _Density;
                float _Scale;
                float4 _Drift;
                float _Soft;
                float _Near;
                half _Fade;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; half4 colour : COLOR; float2 layer : TEXCOORD1; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 world : TEXCOORD0;
                half rim : TEXCOORD1;
                half top : TEXCOORD2;
                float4 screen : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.world = TransformObjectToWorld(input.position.xyz);
                o.position = TransformWorldToHClip(o.world);
                o.rim = input.colour.a;
                o.top = input.layer.x;
                o.screen = ComputeScreenPos(o.position);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.world.xz * _Scale + _Seed;
                float2 drift = _Drift.xy * _Time.y * _Scale;
                float n = Noise(p + drift + i.top * 3.7) * 0.65 + Noise(p * 2.3 - drift * 1.7 + 9.1) * 0.35;
                float body = smoothstep(0.25, 0.75, n);
                // Thick in the middle, gone at the rim; denser low down.
                float shape = i.rim * i.rim * (3 - 2 * i.rim) * lerp(1.0, 0.45, i.top);
                // Soft where it meets the ground, trunks and the water's bed; thinned at the camera.
                float2 uv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = LinearEyeDepth(i.position.z, _ZBufferParams);
                float soft = saturate((scene - self) / max(_Soft, 0.01));
                float near = saturate((self - 0.4) / max(_Near, 0.01));
                float alpha = saturate(_Density * shape * body * soft * near * _Fade);
                half3 colour = lerp(_BaseColor.rgb, unity_FogColor.rgb, _FogShare);
                return half4(WorldGrade(colour), alpha);
            }
            ENDHLSL
        }
    }
}
