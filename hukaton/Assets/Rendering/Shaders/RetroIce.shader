// Ice for Hakaton, built on the Retro Water conventions (same world pixel grid, colour depth, URP fog).
// Procedural; no texture assets required. Needs the Depth and Opaque textures, like Retro Water.
//
// Two uses, chosen by the material:
//   Ice.mat      (_Shell = 0) — a sheet of ice on water: opaque result composited from the opaque texture, writes
//                depth, queue Transparent-90 (just after the water). Flat top + skirt; thickness ~0.12 m.
//   IceShell.mat (_Shell = 1) — a closed faceted shell around a creature sprite: alpha blended over the sprite
//                (sprites are Transparent 3000 and write depth for their hard pixels; the shell sits at 3050, so a
//                drawing in front of a shell hides it and the creature inside shows through), back faces first, then front.
//
// Properties meant to be driven per renderer (MaterialPropertyBlock):
//   _Reveal  0..1  freeze-in. 0 = nothing, crystals grow in as noisy frost, 1 = finished clear ice.
//   _Melt    0..1  thaw. Ice turns wet and glassy, thins, then dissolves in noisy holes with bright wet rims.
//   _Tint    color multiplier for the whole ice (e.g. a flash on hit); default white.
// Optional mesh data (sheet): vertex colour R = edge frost (1 on the rim of a patch) when _UseVertexFrost = 1.
// Side faces (normal not up) of the sheet are frosted automatically.
Shader "Hukaton/Ice/Retro Ice"
{
    Properties
    {
        [Header(Use)]
        [ToggleUI] _Shell("Creature Shell (blended)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [ToggleUI] _ZWrite("ZWrite", Float) = 1
        _BackColorMask("Back Faces Pass (0 off, 15 on)", Float) = 0

        [Header(Driven)]
        _Reveal("Reveal (freeze in)", Range(0, 1)) = 1
        _Melt("Melt", Range(0, 1)) = 0
        _Tint("Tint", Color) = (1, 1, 1, 1)
        [ToggleUI] _UseVertexFrost("Edge Frost From Vertex Colour R", Float) = 0

        [Header(Retro)]
        _PixelsPerMeter("Pixels Per Meter", Range(2, 64)) = 16
        _ColorBitDepth("Color Depth", Range(4, 64)) = 32
        [ToggleUI] _Dither("Dithering", Float) = 0
        _AnimFPS("Animation FPS (0 = smooth)", Range(0, 60)) = 0

        [Header(Body)]
        [HDR] _ClearTint("Clear Tint", Color) = (0.72, 0.95, 1.05, 1)
        [HDR] _DeepColor("Deep Color", Color) = (0.06, 0.28, 0.42, 1)
        _Absorption("Absorption RGB (per m)", Vector) = (1.1, 0.42, 0.28, 0)
        _Refraction("Refraction", Range(0, 0.3)) = 0.09
        _Facets("Facet Size (m)", Range(0.1, 3)) = 0.7
        _FacetTilt("Facet Tilt", Range(0, 0.6)) = 0.16
        _ShellAlpha("Shell Opacity (centre)", Range(0, 1)) = 0.3
        _ShellRimAlpha("Shell Opacity (rim)", Range(0, 1)) = 0.92
        [ToggleUI] _AmbientOverride("Ambient Light Override", Float) = 1
        _AmbientLight("Ambient Light Strength", Range(0, 1)) = 0.5

        [Header(Inside)]
        _CrackScale("Crack Scale", Range(0.1, 6)) = 1.3
        _CrackWidth("Crack Width", Range(0.01, 0.3)) = 0.07
        _CrackStrength("Crack Brightness", Range(0, 3)) = 1.2
        _LayerDepth("Layer Depth (m)", Range(0.01, 0.6)) = 0.07
        _BubbleDensity("Bubbles", Range(0, 1)) = 0.35
        _VeinStrength("Frost Veins", Range(0, 2)) = 0.8
        _FrostColor("Frost Color", Color) = (0.9, 0.97, 1, 1)
        _EdgeFrost("Edge / Shore Frost", Range(0, 1)) = 0.75
        _FrostDepth("Frozen-To-Bottom Depth (m)", Range(0.01, 1)) = 0.25

        [Header(Iridescence)]
        _Iridescence("Iridescence", Range(0, 2)) = 0.85
        _FilmThickness("Film Thickness", Range(0.2, 6)) = 2.2
        _FilmScale("Film Noise Scale", Range(0.05, 4)) = 0.6

        [Header(Light)]
        _Reflection("Reflection Strength", Range(0, 2)) = 0.9
        _ReflectionRoughness("Reflection Roughness", Range(0, 1)) = 0.08
        _Glint("Sun Glint", Range(0, 16)) = 5
        _GlintThreshold("Glint Sharpness", Range(0.9, 0.9999)) = 0.994
        _Sparkle("Sparkles", Range(0, 12)) = 5
        _SparkleDensity("Sparkle Density", Range(0, 1)) = 0.12
        _SparkleSpeed("Sparkle Twinkle Speed", Range(0, 8)) = 2.2
        _Sheen("Moving Sheen", Range(0, 2)) = 0.55
        _SheenSpeed("Sheen Speed", Range(0, 1)) = 0.07
        _Shimmer("Shimmer", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-90"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #define _SURFACE_TYPE_TRANSPARENT 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float _Shell, _SrcBlend, _DstBlend, _ZWrite, _BackColorMask;
        float _Reveal, _Melt, _UseVertexFrost;
        float4 _Tint;
        float _PixelsPerMeter, _ColorBitDepth, _Dither, _AnimFPS;
        float4 _ClearTint, _DeepColor, _Absorption;
        float _Refraction, _Facets, _FacetTilt, _ShellAlpha, _ShellRimAlpha, _AmbientOverride, _AmbientLight;
        float _CrackScale, _CrackWidth, _CrackStrength, _LayerDepth, _BubbleDensity, _VeinStrength;
        float4 _FrostColor;
        float _EdgeFrost, _FrostDepth;
        float _Iridescence, _FilmThickness, _FilmScale;
        float _Reflection, _ReflectionRoughness, _Glint, _GlintThreshold;
        float _Sparkle, _SparkleDensity, _SparkleSpeed, _Sheen, _SheenSpeed, _Shimmer;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float edge : TEXCOORD2;
            float fog : TEXCOORD3;
            float3 centerWS : TEXCOORD4;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.edge = _UseVertexFrost > 0.5 ? v.color.r : 0;
            o.fog = ComputeFogFactor(o.positionCS.z);
            o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
            return o;
        }

        float Hash21(float2 p)
        {
            float3 p3 = frac(p.xyx * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float2 Hash22(float2 p) { return float2(Hash21(p), Hash21(p + float2(17.3, 5.1))); }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p), f = p - i, u = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + 1);
            return a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
        }

        float ValueNoise3(float3 p)
        {
            float3 i = floor(p), f = p - i, u = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0)), n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1)), n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + 1);
            float x00 = lerp(n000, n100, u.x), x10 = lerp(n010, n110, u.x), x01 = lerp(n001, n101, u.x), x11 = lerp(n011, n111, u.x);
            return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
        }

        // Nearest-two Voronoi: x = distance to the border (cracks), yz = id of the nearest cell (facets).
        float3 Voronoi(float2 p)
        {
            float2 i = floor(p), f = p - i;
            float f1 = 9.0, f2 = 9.0;
            float2 id = 0;
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float2 c = float2(x, y);
                float2 o = c + Hash22(i + c) - f;
                float d = dot(o, o);
                if (d < f1) { f2 = f1; f1 = d; id = i + c; }
                else f2 = min(f2, d);
            }
            return float3(sqrt(f2) - sqrt(f1), id);
        }

        float EyeDepth(float rawDepth)
        {
            return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
        }

        float3 SceneWorldPosition(float2 uv, float rawDepth)
        {
            #if !UNITY_REVERSED_Z
            rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
            return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        }

        float3 Quantize(float3 color, float2 pixel)
        {
            float depth = max(2.0, _ColorBitDepth);
            float3 scaled = max(color * depth, 0.0);
            float3 rounded = floor(scaled);
            if (_Dither > 0.5)
            {
                static const float bayer[16] = { 1, 9, 3, 11, 13, 5, 15, 7, 4, 12, 2, 10, 16, 8, 14, 6 };
                uint index = (uint(pixel.x) % 4) * 4 + uint(pixel.y) % 4;
                rounded += step(bayer[index] / 17.0, frac(scaled));
            }
            return rounded / depth;
        }

        float3 Spectrum(float x) { return 0.5 + 0.5 * cos(6.28318 * (x + float3(0.0, 0.33, 0.67))); }

        // Plane coordinates for patterns: the ground plane for the sheet; for the shell, the plane facing the
        // dominant axis of the face normal (faceted shells read as cut crystal).
        float2 PlaneCoords(float3 p, float3 n)
        {
            float3 a = abs(n);
            if (a.y >= a.x && a.y >= a.z) return p.xz;
            return a.x > a.z ? p.zy : p.xy;
        }

        // Cracks, bubbles and veins seen along the view ray inside the ice. Returns rgb = added light, a = whiteness.
        float4 Inside(float3 posWS, float3 V, float3 N, float t)
        {
            float3 into = -V / max(abs(dot(V, N)), 0.25); // ray into the ice, stretched at grazing angles
            float4 acc = 0;
            [unroll] for (int k = 0; k < 3; k++)
            {
                float d = _LayerDepth * (k + 0.6);
                float3 p = posWS + into * d;
                p = (floor(p * _PixelsPerMeter) + 0.5) / _PixelsPerMeter; // each layer on the world pixel grid
                float2 q = PlaneCoords(p, N) + k * float2(3.7, -1.9);
                float fade = 1.0 - k * 0.28;

                // Cracks: a sparse Voronoi web, broken by noise so it never reads as tiles.
                float3 vor = Voronoi(q * _CrackScale * (1.0 + k * 0.45));
                float breakup = step(0.42 - k * 0.06, ValueNoise(q * 1.7 + k * 9.0));
                float crack = smoothstep(_CrackWidth, 0.0, vor.x) * breakup;
                // Bubbles: tiny round pockets in cells.
                float2 bc = q * 9.0 + k * 13.1;
                float2 bi = floor(bc);
                float2 bo = frac(bc) - (0.25 + 0.5 * Hash22(bi + 3.3));
                float bubble = step(1.0 - _BubbleDensity * 0.35, Hash21(bi)) * step(dot(bo, bo), 0.02 + 0.03 * Hash21(bi + 7.0));
                // Frost veins: ridged noise, feathery white.
                float ridge = 1.0 - abs(ValueNoise(q * 2.3 + k * 4.0 + t * 0.01) * 2.0 - 1.0);
                float vein = pow(saturate(ridge), 10.0) * _VeinStrength;

                acc.rgb += (crack * _CrackStrength * float3(0.85, 0.97, 1.1) + bubble * float3(1.1, 1.15, 1.2) * 0.9) * fade;
                acc.a += (vein * 0.6 + crack * 0.35 + bubble * 0.5) * fade;
            }
            return acc;
        }

        float4 Shade(Varyings i, bool backFace)
        {
            float time = _AnimFPS > 0.0 ? floor(_Time.y * _AnimFPS) / _AnimFPS : _Time.y;
            bool shell = _Shell > 0.5;

            float3 Ng = normalize(i.normalWS) * (backFace ? -1.0 : 1.0);
            float3 grid = (floor(i.positionWS * _PixelsPerMeter) + 0.5) / _PixelsPerMeter;
            float2 plane = PlaneCoords(grid, Ng);

            // ---- reveal / melt: dissolve with a crystalline noise ----
            float grain = ValueNoise3(grid * 3.1) * 0.7 + ValueNoise3(grid * 9.7) * 0.3;
            float reveal = saturate(_Reveal);
            clip(reveal * 1.08 - grain - 0.04 * (1.0 - reveal));
            float growFrost = saturate((grain - reveal * 1.08 + 0.28) / 0.28); // fresh crystals are white
            float melt = saturate(_Melt);
            float meltHole = grain - melt * 1.1 + 0.05;
            clip(meltHole);
            float meltRim = saturate(1.0 - meltHole / 0.12) * step(0.001, melt);

            // ---- faceted normal: each Voronoi cell is a slightly tilted facet ----
            float3 facet = Voronoi(plane / _Facets);
            float2 tilt = (Hash22(facet.yz) - 0.5) * 2.0 * _FacetTilt * (1.0 - melt * 0.8);
            float3 T = normalize(cross(Ng, abs(Ng.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
            float3 B = cross(Ng, T);
            float shimmer = (ValueNoise(plane * 1.3 + time * 0.15) - 0.5) * _Shimmer * 0.25;
            float3 N = normalize(Ng + T * (tilt.x + shimmer) + B * (tilt.y - shimmer));

            float3 viewVec = GetCameraPositionWS() - i.positionWS;
            float viewDistance = length(viewVec);
            float3 V = viewVec / max(viewDistance, 1e-4);
            float NdotV = saturate(dot(N, V));

            float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
            float fragEye = EyeDepth(i.positionCS.z);

            Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
            float shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
            float3 ambient = _AmbientOverride > 0.5 ? _AmbientLight.xxx : SampleSH(N);
            float lit = saturate(dot(N, mainLight.direction) * 0.6 + 0.4) * shadow;
            float3 lighting = lerp(ambient, 1.0, lit) * mainLight.color;

            // ---- what is seen through the ice ----
            float2 bend = (N.xz - Ng.xz) + float2(dot(N, T), dot(N, B)) * 0.5;
            float2 refractedUV = screenUV + bend * _Refraction * (shell ? 2.0 : 1.0) / max(fragEye, 1.0) * 6.0;
            float refractedRaw = SampleSceneDepth(refractedUV);
            float rawDepth = SampleSceneDepth(screenUV);
            if (EyeDepth(refractedRaw) < fragEye) { refractedUV = screenUV; refractedRaw = rawDepth; }
            float3 scene = SampleSceneColor(refractedUV);

            float thickness;
            if (shell)
                thickness = 0.12 + (1.0 - NdotV) * 0.35;
            else
            {
                float3 bottomWS = SceneWorldPosition(refractedUV, refractedRaw);
                thickness = max(0.0, i.positionWS.y - bottomWS.y);
            }
            float3 transmit = exp(-thickness * _Absorption.rgb * (1.0 - melt * 0.6));
            float3 color = scene * transmit * _ClearTint.rgb + _DeepColor.rgb * lighting * (1.0 - transmit);
            // The shell is blended over the creature sprite, which the opaque texture does not hold: in the middle
            // the shell is a cold tint over the creature, only towards the rim it bends the world behind like a lens.
            if (shell) color = lerp(_ClearTint.rgb * lighting * 0.55 + _DeepColor.rgb * 0.5, color, pow(1.0 - NdotV, 1.2));

            // ---- inside: cracks, bubbles, frost veins ----
            float4 inside = Inside(i.positionWS, V, Ng, time) * (1.0 - melt * 0.7);
            color += inside.rgb * lighting * 0.55;
            float frost = saturate(inside.a * 0.5);

            // Frost: patch rims, sides of the slab, ice frozen down to a shallow bottom, fresh crystals.
            if (!shell)
            {
                frost = max(frost, saturate(i.edge) * _EdgeFrost);
                frost = max(frost, saturate(1.0 - Ng.y) * _EdgeFrost * 1.2);
                frost = max(frost, smoothstep(_FrostDepth, 0.0, thickness - 0.1) * _EdgeFrost * 0.8);
            }
            else
            {
                // Frosty creases where facets meet.
                frost = max(frost, smoothstep(0.08, 0.0, facet.x) * _EdgeFrost * 0.6);
            }
            float frostNoise = step(0.35, ValueNoise(plane * 7.0));
            frost = saturate(max(frost * (0.65 + 0.35 * frostNoise), growFrost)) * (1.0 - melt * 0.85);
            color = lerp(color, _FrostColor.rgb * lighting * 1.05, frost * 0.85);

            // ---- thin-film iridescence: shifts with the view angle and a slow film-thickness noise ----
            float film = ValueNoise(plane * _FilmScale + time * 0.03) * 1.6;
            float3 irid = Spectrum(NdotV * _FilmThickness + film + facet.y * 0.13);
            float iridMask = saturate(pow(1.0 - NdotV, 2.0) * 1.5 + inside.a * 0.6 + smoothstep(0.1, 0.0, facet.x) * 0.5);
            color += (irid - 0.35) * iridMask * _Iridescence * 0.35 * lighting;

            // ---- reflection: sky + faceted glints ----
            float fresnel = (0.04 + 0.96 * pow(1.0 - NdotV, 5.0)) * _Reflection * (1.0 + melt * 0.6);
            float3 reflection = GlossyEnvironmentReflection(reflect(-V, N), i.positionWS, _ReflectionRoughness, 1.0, screenUV);
            color = lerp(color, reflection, saturate(fresnel));
            float3 H = normalize(mainLight.direction + V);
            color += mainLight.color * step(_GlintThreshold, saturate(dot(N, H))) * _Glint * shadow;

            // Sparkles: single pixels whose hidden micro-facet catches the sun, twinkling.
            float2 sp = floor(plane * _PixelsPerMeter);
            float sh = Hash21(sp + 41.7);
            float3 sn = normalize(N + (float3(Hash21(sp), Hash21(sp + 9.1), Hash21(sp + 3.7)) - 0.5) * 1.2);
            float twinkle = pow(saturate(sin(time * _SparkleSpeed * (0.6 + sh) + sh * 40.0)), 12.0);
            float sparkle = step(1.0 - _SparkleDensity * 0.25, sh) * step(0.9, dot(sn, H)) * twinkle;
            color += mainLight.color * sparkle * _Sparkle * (0.3 + 0.7 * shadow);

            // Slow sheen sweeping across the ice: the satisfying pass of light over a polished surface.
            float sheenBand = frac(dot(plane, float2(0.62, 0.35)) * 0.18 - time * _SheenSpeed);
            float sheen = smoothstep(0.04, 0.0, abs(sheenBand - 0.5)) * (0.3 + 0.7 * pow(1.0 - NdotV, 2.0));
            color += sheen * _Sheen * float3(0.8, 0.95, 1.1) * mainLight.color * 0.5;

            // Thaw: wet, glassy rims around the holes.
            color += meltRim * float3(0.6, 0.85, 1.0) * 0.8;

            color *= _Tint.rgb;

            float alpha = 1.0;
            if (shell)
            {
                float rim = pow(1.0 - NdotV, 1.5);
                alpha = lerp(_ShellAlpha, _ShellRimAlpha, rim);
                alpha = saturate(alpha + frost * 0.45 + inside.a * 0.2 + sparkle + meltRim * 0.3);
                alpha *= (1.0 - melt * 0.5) * _Tint.a;
                if (backFace) alpha *= 0.55;
            }

            color = Quantize(color, i.positionCS.xy);
            color = MixFog(color, i.fog);
            return float4(color, alpha);
        }
        ENDHLSL

        // Shell only: the far side of the crystal, seen through the near side. Off for the sheet (ColorMask 0).
        Pass
        {
            Name "RetroIceBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Front
            ColorMask [_BackColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBack
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            float4 fragBack(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return Shade(i, true);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RetroIce"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return Shade(i, false);
            }
            ENDHLSL
        }

        // Ice on water is solid: it casts shadows and writes depth for the depth prepass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite [_ZWrite]
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            float4 fragDepth(Varyings i) : SV_Target
            {
                float3 grid = (floor(i.positionWS * _PixelsPerMeter) + 0.5) / _PixelsPerMeter;
                float grain = ValueNoise3(grid * 3.1) * 0.7 + ValueNoise3(grid * 9.7) * 0.3;
                clip(saturate(_Reveal) * 1.08 - grain - 0.04 * (1.0 - saturate(_Reveal)));
                clip(grain - saturate(_Melt) * 1.1 + 0.05);
                clip(0.5 - _Shell);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
