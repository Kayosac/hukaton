// Adapted from DEUS Retro Water for URP 17.3. Procedural; no texture assets required.
// Requires Depth Texture (Copy Depth: After Opaques) and Opaque Texture on the active URP asset/camera.
// Use a horizontal water surface. Flow Direction is in local XZ; enable Mesh Flow
// only for meshes whose UV0 stores flow vectors instead of ordinary texture UVs.
Shader "Hukaton/Water/Retro Water"
{
    Properties
    {
        [Header(Retro)]
        _PixelsPerMeter("Pixels Per Meter", Range(2, 64)) = 16
        _ColorBitDepth("Color Depth", Range(4, 64)) = 32
        [ToggleUI] _Dither("Dithering", Float) = 0
        _AnimFPS("Animation FPS (0 = smooth)", Range(0, 60)) = 0

        [Header(Motion)]
        _Speed("Speed", Range(0, 4)) = 1
        [ToggleUI] _UseMeshFlow("Use Mesh UV0 Flow", Float) = 0
        _FlowDirection("Flow Direction (local XZ)", Vector) = (0, 0, 0, 0)
        _FlowSpeed("Flow Speed (m per s)", Range(0, 4)) = 0.6
        _Drift("Still Water Drift (XZ, m per s)", Vector) = (0.12, 0.05, 0, 0)
        _RippleScale("Ripple Scale", Range(0.1, 4)) = 0.9
        _RippleStrength("Ripple Strength", Range(0, 2)) = 0.7
        _RippleFade("Ripple Fade Distance (m)", Range(5, 200)) = 40

        [Header(Body)]
        [HDR] _ShallowTint("Shallow Tint", Color) = (0.85, 1, 0.95, 1)
        [HDR] _DeepColor("Deep Color", Color) = (0.03, 0.16, 0.2, 1)
        _Absorption("Absorption RGB (per m)", Vector) = (1.4, 0.55, 0.38, 0)
        _Refraction("Refraction", Range(0, 0.3)) = 0.06
        [ToggleUI] _AmbientOverride("Ambient Light Override", Float) = 1
        _AmbientLight("Ambient Light Strength", Range(0, 1)) = 0.45

        [Header(Caustics)]
        _CausticScale("Caustic Scale", Range(0.1, 4)) = 1.1
        _CausticStrength("Caustic Strength", Range(0, 6)) = 2.4
        _CausticWidth("Caustic Line Width", Range(0.02, 0.4)) = 0.16
        _CausticFalloff("Caustic Depth Falloff", Range(0, 4)) = 1.2

        [Header(Foam)]
        _FoamColor("Foam Color", Color) = (0.92, 0.97, 1, 1)
        _FoamDepth("Shore Foam Depth (m)", Range(0.02, 2)) = 0.35
        _FoamSpeed("Shore Foam Speed", Range(0, 3)) = 0.6
        _ShoreFoam("Shore Foam Amount", Range(0, 1)) = 0.5
        _FlowFoam("Flow Foam Amount", Range(0, 1)) = 0.35

        [Header(Reflection)]
        _Reflection("Reflection Strength", Range(0, 2)) = 1
        _ReflectionRoughness("Reflection Roughness", Range(0, 1)) = 0.15
        _Glint("Sun Glint", Range(0, 16)) = 4
        _GlintThreshold("Glint Sharpness", Range(0.9, 0.9999)) = 0.996

        [Header(Mire)]
        [ToggleUI] _MireBlend("Mire From Mesh UV1.x", Float) = 0
        [HDR] _MireColor("Mire Color", Color) = (0.12, 0.08, 0.045, 1)
        [HDR] _MireScum("Mire Scum Color", Color) = (0.32, 0.36, 0.12, 1)
        _MireScale("Mire Pattern Scale", Range(0.1, 4)) = 0.8
        _MireSpeed("Mire Churn Speed", Range(0, 1)) = 0.05
        _MireScumAmount("Mire Scum Amount", Range(0, 1)) = 0.35
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
            Name "RetroWater"
            Tags { "LightMode" = "UniversalForward" }
            // Opaque result: the scene below is composited from the opaque texture.
            Blend One Zero
            ZWrite On
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

            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _PixelsPerMeter, _ColorBitDepth, _Dither, _AnimFPS;
            float _Speed, _FlowSpeed, _RippleScale, _RippleStrength, _RippleFade;
            float4 _Drift, _FlowDirection;
            float _UseMeshFlow;
            float4 _ShallowTint, _DeepColor, _Absorption;
            float _Refraction, _AmbientOverride, _AmbientLight;
            float _CausticScale, _CausticStrength, _CausticWidth, _CausticFalloff;
            float4 _FoamColor;
            float _FoamDepth, _FoamSpeed, _ShoreFoam, _FlowFoam;
            float _Reflection, _ReflectionRoughness, _Glint, _GlintThreshold;
            float _MireBlend, _MireScale, _MireSpeed, _MireScumAmount;
            float4 _MireColor, _MireScum;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 flow : TEXCOORD0;
                float2 mire : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 flow : TEXCOORD1;
                float fog : TEXCOORD2;
                float mire : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                // How much bog mire is here (swamp water meshes carry it in UV1; others opt out by the toggle).
                o.mire = _MireBlend > 0.5 ? saturate(v.mire.x) : 0.0;
                // Ordinary UVs are not flow vectors. Opt in for authored river meshes.
                float2 flow = _UseMeshFlow > 0.5 ? v.flow : _FlowDirection.xy;
                float3 flowWS = TransformObjectToWorldDir(float3(flow.x, 0, flow.y), false);
                o.flow = flowWS.xz;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = p - i, u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + 1);
                return a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
            }

            // Value noise gradient (analytic), for ripple normals.
            float2 ValueNoiseGrad(float2 p)
            {
                float2 i = floor(p), f = p - i, u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + 1);
                float k = a - b - c + d;
                return du * float2((b - a) + k * u.y, (c - a) + k * u.x);
            }

            // Distance between the two nearest cell points: ~0 on cell borders, the caustic web.
            float VoronoiEdge(float2 p)
            {
                float2 i = floor(p), f = p - i;
                float f1 = 9.0, f2 = 9.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = float2(x, y);
                    float2 o = c + float2(Hash21(i + c), Hash21(i + c + float2(17.3, 5.1))) - f;
                    float d = dot(o, o);
                    f2 = d < f1 ? f1 : min(f2, d);
                    f1 = min(f1, d);
                }
                return sqrt(f2) - sqrt(f1);
            }

            float EyeDepth(float rawDepth)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float3 SceneWorldPosition(float2 uv, float rawDepth)
            {
                // SampleSceneDepth is 0..1; OpenGL clip-space depth is -1..1.
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

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float time = _AnimFPS > 0.0 ? floor(_Time.y * _AnimFPS) / _AnimFPS : _Time.y;
                float t = time * _Speed;

                // Every pattern lives on one world-space pixel grid, like point-filtered textures.
                float2 cell = (floor(i.positionWS.xz * _PixelsPerMeter) + 0.5) / _PixelsPerMeter;
                float2 flow = i.flow * _FlowSpeed;
                float flowLength = length(flow);

                // Ripples: flow map with two phases so bends never smear the pattern.
                float2 grad = 0;
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float phase = frac(t * 0.5 + k * 0.5);
                    float weight = 1.0 - abs(1.0 - 2.0 * phase);
                    float2 p = cell - flow * phase * 2.0 + _Drift.xy * t + k * float2(7.1, 3.3);
                    grad += weight * (ValueNoiseGrad(p * _RippleScale * 2.0) * 2.0
                        + ValueNoiseGrad(p * _RippleScale * 5.3 + float2(11, -4)) * 2.65) * _RippleScale;
                }
                float3 viewVec = GetCameraPositionWS() - i.positionWS;
                float viewDistance = length(viewVec);
                float3 V = viewVec / max(viewDistance, 1e-4);
                float mire = i.mire;
                // Mire is thick: barely a ripple.
                float rippleK = _RippleStrength * 0.1 * saturate(1.0 - viewDistance / _RippleFade) * (1.0 - 0.8 * mire);
                float3 N = normalize(float3(-grad.x * rippleK, 1.0, -grad.y * rippleK));

                // Scene below: depth and colour, refracted along the ripple normal.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float fragEye = EyeDepth(i.positionCS.z);
                float rawDepth = SampleSceneDepth(screenUV);
                float3 bottomWS = SceneWorldPosition(screenUV, rawDepth);
                float depth = max(0.0, i.positionWS.y - bottomWS.y);

                float2 refractedUV = screenUV + N.xz * _Refraction * saturate(depth * 2.0) / max(fragEye, 1.0);
                float refractedRaw = SampleSceneDepth(refractedUV);
                // Never refract things standing in front of the water.
                if (EyeDepth(refractedRaw) < fragEye) { refractedUV = screenUV; refractedRaw = rawDepth; }
                float3 refractedWS = SceneWorldPosition(refractedUV, refractedRaw);
                float refractedDepth = max(0.0, i.positionWS.y - refractedWS.y);
                float3 scene = SampleSceneColor(refractedUV);

                // Surface lighting uses URP directly, with no DEUS scene globals.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                float lightAmount = saturate(dot(N, mainLight.direction)) * shadow;
                float3 ambient = _AmbientOverride > 0.5 ? _AmbientLight.xxx : SampleSH(N);
                float3 lighting = lerp(ambient, 1.0, lightAmount) * mainLight.color;

                // Caustics on the bottom, on the pixel grid there too, only where the sun reaches it.
                float2 bottomCell = (floor(refractedWS.xz * _PixelsPerMeter) + 0.5) / _PixelsPerMeter;
                float c1 = VoronoiEdge(bottomCell * _CausticScale + float2(0.23, -0.11) * t);
                float c2 = VoronoiEdge(bottomCell * _CausticScale * 1.37 + float2(5.0 - 0.17 * t, 0.19 * t));
                float caustics = (smoothstep(_CausticWidth, 0.0, c1) + smoothstep(_CausticWidth, 0.0, c2)) * 0.5;
                float bottomLit = GetMainLight(TransformWorldToShadowCoord(refractedWS)).shadowAttenuation;
                caustics *= _CausticStrength * exp(-refractedDepth * _CausticFalloff) * smoothstep(0.0, 0.12, refractedDepth) * bottomLit;

                // Body: light absorbed per channel with depth, scattered back as the deep colour.
                float3 transmit = exp(-refractedDepth * _Absorption.rgb);
                float3 color = scene * (1.0 + caustics * mainLight.color) * transmit * _ShallowTint.rgb
                    + _DeepColor.rgb * lighting * (1.0 - transmit);

                // Bog mire: opaque brown slurry churning slowly, blotched darker, a sour green scum drifting on it.
                if (mire > 0.001)
                {
                    float2 mp = cell * _MireScale;
                    float mt = time * _MireSpeed;
                    float churn = ValueNoise(mp * 1.3 + float2(mt, -mt * 0.7)) * 0.65 + ValueNoise(mp * 3.1 + float2(-mt * 1.3, mt)) * 0.35;
                    float scum = step(1.0 - _MireScumAmount, ValueNoise(mp * 2.2 + float2(13.0 - mt * 0.6, 4.0 + mt * 0.4)));
                    float3 slurry = _MireColor.rgb * lighting * (0.7 + 0.6 * churn);
                    slurry = lerp(slurry, _MireScum.rgb * lighting, scum * 0.7 * smoothstep(0.35, 0.9, mire));
                    color = lerp(color, slurry, smoothstep(0.0, 1.0, mire));
                }

                // Foam: broken bands rolling onto the shore, and streaks along the current.
                float foamNoise = ValueNoise(cell * 3.0 + float2(0.3, -0.2) * t);
                float edge = smoothstep(_FoamDepth, 0.0, depth);
                float bands = frac(depth / _FoamDepth * 2.0 + t * _FoamSpeed + foamNoise * 0.6);
                float shore = max(step(depth, 0.05) * step(0.3, foamNoise),
                    step(1.0 - edge * 0.6, bands) * step(0.25, edge) * step(0.35, foamNoise)) * step(0.001, _ShoreFoam)
                    * step(1.0 - _ShoreFoam * 2.0, foamNoise);
                float2 along = flowLength > 1e-4 ? flow / flowLength : float2(1, 0);
                float2 q = float2(dot(cell, along) * 1.1 - t * flowLength * 2.0, dot(cell, float2(-along.y, along.x)) * 7.0);
                float flowFoam = step(1.0 - _FlowFoam * saturate(flowLength) * 0.5, ValueNoise(q)) * saturate(flowLength * 2.0);
                float foam = saturate(shore + flowFoam * 0.8);

                // Sky reflection and pixel glints of the sun.
                float NdotV = saturate(dot(N, V));
                float fresnel = (0.02 + 0.98 * pow(1.0 - NdotV, 5.0)) * _Reflection * (1.0 - 0.6 * mire);
                float3 reflection = GlossyEnvironmentReflection(reflect(-V, N), i.positionWS, _ReflectionRoughness, 1.0, screenUV);
                color = lerp(color, reflection, saturate(fresnel));
                float3 H = normalize(mainLight.direction + V);
                color += mainLight.color * step(_GlintThreshold, saturate(dot(N, H))) * _Glint * shadow;

                color = lerp(color, _FoamColor.rgb * lighting, foam * 0.9 * (1.0 - mire));
                color = Quantize(color, i.positionCS.xy);
                color = MixFog(color, i.fog);
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
