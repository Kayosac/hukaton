// Soap film for the «Пузырь» card dome. Alpha blended, both sides (inner film first, then the outer), no depth
// writes. Thin-film iridescence swirling over the surface and draining toward the ground, a bright rim, a hard sun
// glint and a soft "window" highlight, a glowing line where the film meets the ground (scene depth), an idle
// wobble, a jiggle (_Kick) and a ripple running out from the last pop (_RippleCentre/_RippleAge). Colours are
// quantised like Retro Water/Ice; URP fog applies.
// Driven per renderer (MaterialPropertyBlock): _Alpha, _Kick, _RippleCentre, _RippleAge.
Shader "Hakaton/Soap Bubble"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (0.8, 0.95, 1.05, 1)
        _Alpha ("Alpha (driven)", Range(0, 1)) = 1

        [Header(Film)]
        _FacingAlpha ("Opacity facing the view", Range(0, 1)) = 0.05
        _RimAlpha ("Opacity at the rim", Range(0, 1)) = 0.8
        _RimPower ("Rim falloff", Range(0.5, 8)) = 2.4
        _BackAlpha ("Inner side opacity", Range(0, 1)) = 0.5
        _Iridescence ("Iridescence", Range(0, 2)) = 1.15
        _FilmThickness ("Film thickness (colour bands)", Range(0.2, 8)) = 3.1
        _FilmScale ("Swirl scale", Range(0.1, 8)) = 1.7
        _FilmSpeed ("Swirl speed", Range(0, 2)) = 0.22
        _Drain ("Drain to the ground", Range(0, 2)) = 0.8

        [Header(Light)]
        _Glint ("Sun glint", Range(0, 8)) = 3.5
        _GlintSharpness ("Sun glint sharpness", Range(8, 1024)) = 260
        _Window ("Window highlight", Range(0, 2)) = 0.9
        _ContactGlow ("Ground contact glow", Range(0, 4)) = 1.4
        _ContactWidth ("Ground contact width (m)", Range(0.01, 1)) = 0.22

        [Header(Motion)]
        _Wobble ("Idle wobble (share of radius)", Range(0, 0.1)) = 0.016
        _WobbleSpeed ("Wobble speed", Range(0, 8)) = 1.6
        _Kick ("Jiggle (driven)", Range(0, 0.3)) = 0
        _RippleCentre ("Ripple centre, world (driven)", Vector) = (0, 0, 0, 0)
        _RippleAge ("Ripple age, s (driven)", Float) = 100
        _RippleSpeed ("Ripple speed, m/s", Range(0.5, 20)) = 6
        _RippleStrength ("Ripple strength", Range(0, 2)) = 1

        [Header(Retro)]
        _ColorBitDepth ("Colour depth", Range(4, 64)) = 24
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #define _SURFACE_TYPE_TRANSPARENT 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _Tint;
        float _Alpha;
        float _FacingAlpha, _RimAlpha, _RimPower, _BackAlpha, _Iridescence, _FilmThickness, _FilmScale, _FilmSpeed, _Drain;
        float _Glint, _GlintSharpness, _Window, _ContactGlow, _ContactWidth;
        float _Wobble, _WobbleSpeed, _Kick;
        float4 _RippleCentre;
        float _RippleAge, _RippleSpeed, _RippleStrength;
        float _ColorBitDepth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
            float ripple : TEXCOORD3;
            float fog : TEXCOORD4;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float Noise3(float3 p)
        {
            float3 i = floor(p), f = p - i, u = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0)), n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1)), n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + 1);
            float x00 = lerp(n000, n100, u.x), x10 = lerp(n010, n110, u.x), x01 = lerp(n001, n101, u.x), x11 = lerp(n011, n111, u.x);
            return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
        }

        float3 Spectrum(float x) { return 0.5 + 0.5 * cos(6.28318 * (x + float3(0.0, 0.33, 0.67))); }

        float RippleRing(float3 positionWS)
        {
            if (_RippleAge > 3.0) return 0;
            float d = distance(positionWS, _RippleCentre.xyz);
            float front = d - _RippleAge * _RippleSpeed;
            return exp(-front * front * 5.0) * exp(-_RippleAge * 2.2) * _RippleStrength;
        }

        Varyings vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 p = v.positionOS.xyz;
            float3 n = normalize(v.normalOS);
            float t = _Time.y * _WobbleSpeed;
            // The rim on the ground stays put; the film above breathes and jiggles.
            float lift = saturate(p.y * 3.0);
            float idle = sin(p.x * 3.1 + t) * sin(p.z * 2.7 - t * 1.3) + sin(p.y * 4.3 + t * 0.7) * 0.6;
            float jiggle = sin(_Time.y * 19.0 + p.y * 5.0 + p.x * 3.0) * _Kick;
            float3 world = TransformObjectToWorld(p);
            float ring = RippleRing(world);
            p += n * (idle * _Wobble + jiggle + ring * 0.03) * lift;
            o.positionOS = v.positionOS.xyz;
            o.positionWS = TransformObjectToWorld(p);
            o.normalWS = TransformObjectToWorldNormal(n);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.ripple = ring;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        float3 Quantize(float3 colour)
        {
            float depth = max(2.0, _ColorBitDepth);
            return floor(max(colour * depth, 0.0) + 0.5) / depth;
        }

        float4 Shade(Varyings i, bool inner)
        {
            float3 N = normalize(i.normalWS);
            if (inner) N = -N;
            float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
            float NdotV = saturate(abs(dot(N, V)));
            float rim = pow(1.0 - NdotV, _RimPower);

            // Thin film: thickness swirls over the surface and drains down toward the ground.
            float time = _Time.y * _FilmSpeed;
            float3 q = i.positionOS * _FilmScale;
            float swirl = Noise3(q + float3(0, time, time * 0.6)) + 0.5 * Noise3(q * 2.3 - float3(time * 0.8, 0, time));
            float drain = (1.0 - saturate(i.positionOS.y)) * _Drain;
            float thickness = _FilmThickness * (0.55 + drain) + swirl * 1.6 + i.ripple * 0.8;
            float3 film = Spectrum(thickness * (1.25 - NdotV * 0.5));

            Light sun = GetMainLight();
            float3 H = normalize(sun.direction + V);
            float glint = step(0.5, pow(saturate(dot(N, H)), _GlintSharpness)) * _Glint;
            float3 R = reflect(-V, N);
            float window = smoothstep(0.82, 0.9, dot(R, normalize(sun.direction + float3(0.25, 0.6, -0.2)))) * _Window;

            // Where the film meets the ground (or a rock poking through): a bright wet line.
            float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
            float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
            float selfEye = -TransformWorldToView(i.positionWS).z;
            float contact = (1.0 - saturate((sceneEye - selfEye) / max(0.001, _ContactWidth))) * step(selfEye, sceneEye + 0.05);

            float3 sky = _Tint.rgb * (0.55 + 0.45 * sun.color);
            float3 colour = lerp(sky, film * sun.color, saturate(_Iridescence * (0.35 + rim)));
            colour += (glint + window) * sun.color;
            colour += film * (contact * _ContactGlow + i.ripple * 0.8);

            float alpha = lerp(_FacingAlpha, _RimAlpha, rim);
            alpha = saturate(alpha + glint * 0.5 + window * 0.35 + contact * 0.5 * _ContactGlow + i.ripple * 0.35);
            if (inner) alpha *= _BackAlpha;
            alpha *= _Alpha * _Tint.a;

            colour = Quantize(colour);
            colour = MixFog(colour, i.fog);
            return float4(colour, alpha);
        }
        ENDHLSL

        Pass
        {
            Name "SoapBubbleInner"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragInner
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            float4 fragInner(Varyings i) : SV_Target { return Shade(i, true); }
            ENDHLSL
        }

        Pass
        {
            Name "SoapBubbleOuter"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragOuter
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            float4 fragOuter(Varyings i) : SV_Target { return Shade(i, false); }
            ENDHLSL
        }
    }
    Fallback Off
}
