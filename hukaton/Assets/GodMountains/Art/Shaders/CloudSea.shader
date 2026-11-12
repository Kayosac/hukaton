// The sea of clouds under the god mountains. A horizontal plane is the top of a layer of cloud; every pixel marches
// its view ray into a height field of billows (Worley domes over a smooth macro field) that drifts with the wind and
// slowly boils (two phases of the big puffs morph into each other), then lights the hit point: wrap light from the
// sun, self shadow toward the sun, dark deep valleys, a silver lining and warm forward scatter against the sun, in a
// few dithered bands like the game's pixel sky. Where the clouds meet rock they soften and wrap it in a bright collar
// of mist (scene depth). One ray per virtual pixel, the same grid as the sky. Requires the Depth Texture.
Shader "Hukaton/God Mountains/Cloud Sea"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex("Noise (R big puffs, G macro, B detail, A small puffs)", 2D) = "gray" {}

        [Header(Shape)]
        _Thickness("Layer thickness (m)", Range(10, 400)) = 120
        _PuffScale("Big puffs tile (m)", Range(100, 6000)) = 2400
        _SmallScale("Small puffs tile (m)", Range(50, 3000)) = 760
        _MacroScale("Macro tile (m)", Range(1000, 40000)) = 14000
        _DetailScale("Detail tile (m)", Range(10, 1000)) = 120
        _Coverage("Coverage", Range(0, 1)) = 0.62
        _Puffiness("Puffiness", Range(0.5, 3)) = 1.4

        [Header(Motion)]
        _Wind("Wind XZ (m per s)", Vector) = (7, 2.5, 0, 0)
        _Boil("Boil (cycles per s)", Range(0, 0.2)) = 0.018
        _AnimFPS("Animation FPS (0 = smooth)", Range(0, 30)) = 0
        _TimeOffset("Time offset (s, previews)", Float) = 0

        [Header(Light)]
        _LitColor("Lit", Color) = (0.965, 0.94, 0.98, 1)
        _MidColor("Mid", Color) = (0.84, 0.81, 0.93, 1)
        _ShadowColor("Shadow", Color) = (0.66, 0.62, 0.79, 1)
        _DeepColor("Deep valleys", Color) = (0.5, 0.46, 0.66, 1)
        _SunTint("Sun tint", Color) = (1, 0.94, 0.85, 1)
        _RimColor("Silver lining", Color) = (1, 0.97, 0.93, 1)
        _Rim("Silver lining strength", Range(0, 2)) = 0.55
        _Scatter("Forward scatter toward the sun", Range(0, 2)) = 0.45
        _Wrap("Light wrap", Range(0, 1)) = 0.45
        _ShadowLength("Self shadow length (m)", Range(0, 600)) = 220
        _Steps("Shading bands (0 = smooth)", Range(0, 12)) = 5
        _BandDither("Band dither", Range(0, 1)) = 0.6

        [Header(Contact)]
        _SoftDepth("Soft contact (m)", Range(0.1, 100)) = 14
        _CollarWidth("Mist collar width (m)", Range(0, 200)) = 45
        _Collar("Mist collar strength", Range(0, 1)) = 0.55

        [Header(Retro)]
        _PixelResolution("Vertical pixels (0 = native)", Range(0, 1080)) = 360
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CloudSea"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
            float _Thickness, _PuffScale, _SmallScale, _MacroScale, _DetailScale, _Coverage, _Puffiness;
            float4 _Wind;
            float _Boil, _AnimFPS, _TimeOffset;
            float4 _LitColor, _MidColor, _ShadowColor, _DeepColor, _SunTint, _RimColor;
            float _Rim, _Scatter, _Wrap, _ShadowLength, _Steps, _BandDither;
            float _SoftDepth, _CollarWidth, _Collar;
            float _PixelResolution;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            static float gTime;
            static float gTop;

            float Hash21(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float4 Noise(float2 uv, float mip)
            {
                return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uv, mip);
            }

            // Mip for a texture tiled every `tile` metres, seen `dist` metres away (one screen pixel ~ 0.0022 dist).
            float Mip(float tile, float dist)
            {
                return clamp(log2(max(dist * 0.0022 * 512.0 / tile, 1e-4)), 0.0, 8.0);
            }

            // Big puffs that never just slide: two phases, each drifting a little and jumping to a new place while
            // its weight is zero, cross-faded (a flow-map trick) — the billows swell and sink.
            float BigPuffs(float2 xz, float dist)
            {
                float2 uv = (xz - _Wind.xy * gTime) / _PuffScale;
                float boil = gTime * _Boil;
                float mip = Mip(_PuffScale, dist);
                float a = 0;
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float phase = boil + k * 0.5;
                    float cycle = floor(phase);
                    float f = phase - cycle;
                    float w = 1.0 - abs(1.0 - 2.0 * f);
                    float2 jump = float2(Hash21(float2(cycle, k * 7.3)), Hash21(float2(k * 3.1, cycle + 11.0)));
                    float2 drift = float2(0.06, -0.04) * f;
                    a += w * Noise(uv + jump + drift, mip).r;
                }
                return a;
            }

            // Cloud surface height (world y) and its 0..1 fraction of the layer.
            float Height(float2 xz, float dist, out float frac01)
            {
                float macro = Noise(xz / _MacroScale + _Wind.xy * gTime / _MacroScale * 0.3, 0).g;
                float big = BigPuffs(xz, dist);
                float small = Noise((xz - _Wind.xy * gTime * 1.25) / _SmallScale + float2(0.31, 0.77), Mip(_SmallScale, dist)).a;
                float detailFade = saturate(1.0 - dist / 4000.0);
                float detail = Noise((xz - _Wind.xy * gTime * 1.6) / _DetailScale, Mip(_DetailScale, dist)).b;
                float cover = saturate(_Coverage + (macro - 0.5) * 1.1);
                big = smoothstep(0.18, 0.95, big);
                float d = big * 0.68 + small * 0.22 + (detail - 0.5) * 0.1 * detailFade + 0.06;
                // Coverage lifts the field: high — puffs merge into a sea; low — open dark valleys between towers.
                float h = saturate((d - (1.0 - cover) * 0.75) / max(0.25 + cover * 0.75, 0.05));
                h = pow(h, 1.0 / _Puffiness);
                frac01 = h;
                return gTop - _Thickness * (1.0 - h);
            }

            float HeightOnly(float2 xz, float dist)
            {
                float f;
                return Height(xz, dist, f);
            }

            float Bayer4(float2 pixel)
            {
                static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                uint index = (uint(pixel.x) % 4) * 4 + uint(pixel.y) % 4;
                return (bayer[index] + 0.5) / 16.0;
            }

            float EyeDepth(float rawDepth)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float now = _Time.y + _TimeOffset;
                gTime = _AnimFPS > 0.0 ? floor(now * _AnimFPS) / _AnimFPS : now;
                gTop = i.positionWS.y;
                float3 cam = GetCameraPositionWS();

                // One ray per virtual pixel: move the entry point to the pixel's centre along the plane.
                float2 pixel = i.positionCS.xy;
                float2 snapped = pixel;
                if (_PixelResolution > 0.5)
                {
                    float size = max(1.0, floor(_ScaledScreenParams.y / _PixelResolution + 0.5));
                    snapped = (floor(pixel / size) + 0.5) * size;
                }
                float3 entry = i.positionWS + ddx(i.positionWS) * (snapped.x - pixel.x) + ddy(i.positionWS) * (snapped.y - pixel.y);
                entry.y = gTop;
                float3 dir = normalize(entry - cam);
                if (dir.y > -1e-4) discard;
                float entryDist = length(entry - cam);

                // March down into the height field; steps shrink near the surface and grow with distance.
                float tExit = _Thickness / -dir.y;
                float tA = 0.0, tB = tExit, t = 0.0;
                bool hit = false;
                float maxStep = lerp(30.0, 420.0, saturate(entryDist / 7000.0));
                [loop] for (int s = 0; s < 56; s++)
                {
                    float3 p = entry + dir * t;
                    float gap = p.y - HeightOnly(p.xz, entryDist + t);
                    if (gap <= 0.0) { hit = true; tB = t; break; }
                    tA = t;
                    t += clamp(gap / -dir.y * 0.6, 0.4, maxStep);
                    if (t >= tExit) { t = tExit; tB = tExit; break; }
                }
                if (hit)
                {
                    [unroll] for (int r = 0; r < 5; r++)
                    {
                        float tm = (tA + tB) * 0.5;
                        float3 p = entry + dir * tm;
                        if (p.y - HeightOnly(p.xz, entryDist + tm) > 0.0) tA = tm; else tB = tm;
                    }
                }
                t = tB;
                float3 hitWS = entry + dir * t;
                float dist = entryDist + t;
                float h01;
                Height(hitWS.xz, dist, h01);

                // Surface normal from the height field, coarser far away.
                float e = max(3.0, dist * 0.004);
                float hx = HeightOnly(hitWS.xz + float2(e, 0), dist) - HeightOnly(hitWS.xz - float2(e, 0), dist);
                float hz = HeightOnly(hitWS.xz + float2(0, e), dist) - HeightOnly(hitWS.xz - float2(0, e), dist);
                float3 n = normalize(float3(-hx, 2.0 * e, -hz));

                Light sun = GetMainLight();
                float3 L = sun.direction;
                // Self shadow: billows between the point and the sun.
                float shadow = 1.0;
                [unroll] for (int k = 1; k <= 4; k++)
                {
                    float3 q = hitWS + L * (_ShadowLength * k / 4.0) + n * 2.0;
                    float above = HeightOnly(q.xz, dist) - q.y;
                    shadow -= saturate(above / 18.0) * 0.26;
                }
                shadow = saturate(shadow);

                float wrap = saturate((dot(n, L) + _Wrap) / (1.0 + _Wrap));
                float ao = smoothstep(0.0, 0.9, h01);
                float shade = wrap * lerp(0.35, 1.0, shadow) * lerp(0.55, 1.0, ao);
                if (_Steps > 0.5)
                {
                    float bands = floor(_Steps);
                    shade = floor(shade * bands + 0.5 + (Bayer4(snapped / max(1.0, floor(_ScaledScreenParams.y / max(_PixelResolution, 1.0) + 0.5))) - 0.5) * _BandDither) / bands;
                    shade = saturate(shade);
                }
                float3 col = shade < 0.5 ? lerp(_ShadowColor.rgb, _MidColor.rgb, shade * 2.0)
                                         : lerp(_MidColor.rgb, _LitColor.rgb * _SunTint.rgb, shade * 2.0 - 1.0);
                col = lerp(_DeepColor.rgb, col, lerp(0.35, 1.0, ao));

                float toSun = saturate(dot(dir, L));
                float rim = pow(1.0 - saturate(dot(n, -dir)), 3.0) * (0.35 + 0.65 * toSun * toSun) * shadow;
                float scatter = pow(toSun, 8.0) * (1.0 - h01 * 0.5);
                col += _RimColor.rgb * rim * _Rim + _SunTint.rgb * scatter * _Scatter;
                col *= lerp(float3(1, 1, 1), sun.color, 0.35);

                // Contact with rock: soft edge and a bright collar of mist hugging it.
                float2 screenUV = GetNormalizedScreenSpaceUV(snapped);
                float sceneEye = EyeDepth(SampleSceneDepth(screenUV));
                float hitEye = dot(hitWS - cam, -UNITY_MATRIX_V[2].xyz);
                float gap = sceneEye - hitEye;
                clip(gap);
                float alpha = saturate(gap / _SoftDepth);
                float collar = (1.0 - saturate(gap / max(_CollarWidth, 0.01))) * _Collar;
                col = lerp(col, _LitColor.rgb * 1.04, collar);

                col = MixFog(col, ComputeFogFactor(TransformWorldToHClip(hitWS).z));
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
