Shader "Hukaton/Sprites/Hand Drawn Pixel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _SpriteUVRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _Color ("Tint", Color) = (1,1,1,1)
        [Header(Crisp Pixels)]
        _PixelsPerUnit ("Pixels Per World Unit", Float) = 500
        _AlphaCutoff ("Hard Alpha Cutoff", Range(0.01,1)) = 0.5
        // Close up the world grid would turn into huge blocks (the shop counter, an item held in hand),
        // so a cell never grows past this size on screen: it splits in two, four... (nested in the world grid).
        _MaxCellPixels ("Max Cell On Screen (px of a 1080-line view)", Range(0.5,16)) = 3
        // Two splits = 4x the world grid: customers (2x) and loot drawings (4x) are authored at those densities.
        _MaxRefine ("Max Close-up Splits (powers of two)", Range(0,6)) = 2
        [Header(Hand Drawn Motion)]
        _Motion ("Motion Amount", Range(0,2)) = 1
        _AnimationFPS ("Drawing Frames Per Second", Range(1,30)) = 8
        _Speed ("Breathing Speed", Range(0,4)) = 1
        _Sway ("Sway (pixel cells)", Range(0,4)) = 0.8
        _Breath ("Breathing Stretch", Range(0,0.04)) = 0.008
        _Boil ("Line Wobble (pixel cells)", Range(0,1)) = 0.18
        _Anchor ("Foot Anchor (texture Y)", Range(0,0.5)) = 0.08
        _Seed ("Motion Phase", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        // Depth: coverage is binary, so the drawing writes depth and ice shells drawn after it are hidden behind it.
        // Off for plates that carry text on top (speech bubbles).
        [Header(Depth)]
        [ToggleUI] _ZWrite ("Write Depth", Float) = 1
        // A pulsing outline of hard cells around the drawing (hints: «Потяни меня» and its rope). Off by default:
        // ordinary sprites compile without it. Renderers fade it with _OutlineStrength in a MaterialPropertyBlock.
        [Header(Hint Outline)]
        [Toggle(_HINT_OUTLINE)] _HintOutline ("Pulsing Hint Outline", Float) = 0
        _OutlineColor ("Outline Colour", Color) = (1, 0.8, 0.35, 1)
        _OutlineCells ("Outline Width (world pixel cells)", Range(1,4)) = 2
        _OutlinePulseSpeed ("Pulses Per Second", Range(0,4)) = 1.1
        _OutlineMinAlpha ("Pulse Low Alpha", Range(0,1)) = 0.12
        _OutlineStrength ("Outline Strength (per renderer)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False"
            "PreviewType"="Plane"
        }
        Cull Off
        // Hard pixels: what survives the alpha cutoff is solid, so it may write depth (see _ZWrite).
        ZWrite [_ZWrite]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        // Explicit point sampling at mip zero: sharp even on bilinear source textures.
        // sampler_PointClamp is supplied by URP's GlobalSamplers.hlsl.
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float4 _SpriteUVRect;
            float _PixelsPerUnit, _AlphaCutoff, _MaxCellPixels, _MaxRefine, _Motion, _AnimationFPS;
            float _Speed, _Sway, _Breath, _Boil, _Anchor, _Seed;
            half4 _OutlineColor;
            float _OutlineCells, _OutlinePulseSpeed, _OutlineMinAlpha, _OutlineStrength;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionRelativeWS : TEXCOORD1;
            float fogFactor : TEXCOORD2;
            float refine : TEXCOORD3;
            half4 color : COLOR;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            SetUpSpriteInstanceProperties();
            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            // Use the actual rendering view, even when a camera overrides its view matrix.
            // Its Transform and _WorldSpaceCameraPos need not then match that view.
            // Large interpolated world coordinates otherwise lose derivative precision.
            float3 viewOriginWS = GetViewToWorldMatrix()._m03_m13_m23;
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            output.positionRelativeWS = positionWS - viewOriginWS;
            // Scene fog (Lighting > Environment) by view depth, like the rest of the world.
            output.fogFactor = ComputeFogFactorZ0ToFar(max(0.0, -TransformWorldToView(positionWS).z));
            // Close-up split, one factor for the whole drawing (from its pivot, so no seam can cross it):
            // a world cell's height on screen in pixels of a 1080-line view (angular, so any resolution looks alike),
            // halved until it is at most _MaxCellPixels. Far away nothing changes: the world grid holds.
            float pivotW = max(TransformWorldToHClip(TransformObjectToWorld(float3(0, 0, 0))).w, 1e-4);
            float cellPixels = 540.0 * abs(UNITY_MATRIX_P._m11) / (pivotW * max(_PixelsPerUnit, 0.001));
            float splits = ceil(log2(max(cellPixels / max(_MaxCellPixels, 0.01), 1.0)) - 1e-4);
            output.refine = exp2(clamp(splits, 0.0, max(_MaxRefine, 0.0)));
            output.color = input.color * _Color * unity_SpriteColor;
            return output;
        }

        float2 WorldPixelCell(float2 localUV, float3 positionWS)
        {
            // Recover the two full-cell world-space axes from the UV/world Jacobian.
            // Works with sprite PPU, atlas rectangles, flips, parent scale and batching;
            // no per-creature size/property block or per-frame CPU update is needed.
            float2 uvX = ddx(localUV), uvY = ddy(localUV);
            float3 worldX = ddx(positionWS), worldY = ddy(positionWS);
            float determinant = abs(uvX.x * uvY.y - uvX.y * uvY.x);
            float2 axisLengths = float2(
                length(worldX * uvY.y - worldY * uvX.y),
                length(worldY * uvX.x - worldX * uvY.x));
            // The common determinant cancels when converting one world pixel to local UV.
            // Keep a tiny/edge-on sprite finite, and allow at least one cell on either axis.
            return clamp(determinant / max(axisLengths * max(_PixelsPerUnit, 0.001), 1e-12), 1e-7, 1.0);
        }

        #if defined(_HINT_OUTLINE)
        // Alpha of the source texel under a sprite-local UV, zero outside the drawing (same snapping as the drawing).
        half DrawingAlpha(float2 uv)
        {
            float2 rectSize = max(_SpriteUVRect.zw, float2(0.00001, 0.00001));
            float2 inset = _MainTex_TexelSize.xy * 0.5;
            float2 atlasUV = clamp(_SpriteUVRect.xy + uv * rectSize,
                _SpriteUVRect.xy + inset, _SpriteUVRect.xy + rectSize - inset);
            atlasUV = (floor(atlasUV * _MainTex_TexelSize.zw + 0.01) + 0.5) * _MainTex_TexelSize.xy;
            half a = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, atlasUV, 0).a;
            return min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) < 0.0 ? 0.0 : a;
        }

        // Cells within _OutlineCells world cells of the drawing: the hint colour, its alpha pulsing over time.
        half4 HintOutline(float2 uv, float2 worldCell, Varyings input)
        {
            half strength = saturate(_OutlineStrength) * input.color.a;
            clip(strength - 0.002);
            float reach = clamp(_OutlineCells, 1.0, 4.0);
            int r = (int)ceil(reach);
            half near = 0;
            [loop] for (int y = -r; y <= r; y++)
            {
                [loop] for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > reach * reach + 0.5) continue;
                    near = max(near, step(_AlphaCutoff, DrawingAlpha(uv + float2(x, y) * worldCell)));
                }
            }
            clip(near - 0.5);
            float pulse = 0.5 + 0.5 * sin(_Time.y * _OutlinePulseSpeed * 6.2831853);
            half alpha = lerp(_OutlineMinAlpha, 1.0, pulse) * _OutlineColor.a * strength;
            return half4(MixFog(_OutlineColor.rgb, input.fogFactor), alpha);
        }
        #endif

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            // Sprite sheets share one texture: animate inside this sprite's cell, around its own feet.
            float2 rectSize = max(_SpriteUVRect.zw, float2(0.00001, 0.00001));
            // Quantize BEFORE animation: deformation cannot bend or blur the pixel grid.
            float2 localUV = (input.uv - _SpriteUVRect.xy) / rectSize;
            // World grid, split by a power of two close up: the finer grid keeps every world-grid line.
            float2 worldCell = WorldPixelCell(localUV, input.positionRelativeWS);
            float2 cell = worldCell / max(input.refine, 1.0);
            float2 uv = (floor(localUV / cell) + 0.5) * cell;
            float t = floor(_Time.y * max(_AnimationFPS, 1.0)) / max(_AnimationFPS, 1.0);
            float phase = t * _Speed * 2.0 + _Seed;
            float height = saturate((uv.y - _Anchor) / max(1.0 - _Anchor, 0.01));
            float weight = height * height;

            // Inverse warp around the feet. Full Rect sprites keep the moving outline intact.
            uv.y = _Anchor + (uv.y - _Anchor) / (1.0 + sin(phase) * _Breath * _Motion);
            uv.x -= sin(phase * 0.73) * _Sway * worldCell.x * weight * _Motion;
            uv.x = 0.5 + (uv.x - 0.5) / (1.0 - sin(phase) * _Breath * 0.4 * _Motion * height);

            // Low-frequency coherent wobble, held for each drawing frame (no random flicker).
            float2 wobble = float2(
                sin(uv.y * 23.0 + phase * 1.71) + sin(uv.x * 17.0 - phase * 1.13),
                sin(uv.x * 21.0 + phase * 1.37) + sin(uv.y * 13.0 - phase * 1.91));
            uv += wobble * 0.5 * worldCell * _Boil * height * _Motion;

            #if defined(_HINT_OUTLINE)
            // The outline lives in the drawing's transparent margin: test it before anything is rejected.
            if (min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) < 0.0 || DrawingAlpha(uv) < _AlphaCutoff)
                return HintOutline(uv, worldCell, input);
            #endif
            // Reject outside the source; clamping alone would smear opaque edge texels.
            clip(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)));
            // Keep sampling inside this cell even at an exact edge (never the neighbouring pose).
            float2 inset = _MainTex_TexelSize.xy * 0.5;
            float2 atlasUV = clamp(_SpriteUVRect.xy + uv * rectSize,
                _SpriteUVRect.xy + inset, _SpriteUVRect.xy + rectSize - inset);
            // Land on source texel centres explicitly. Derivative roundoff around an integer
            // texel edge must not choose different texels inside the same world pixel.
            atlasUV = (floor(atlasUV * _MainTex_TexelSize.zw + 0.01) + 0.5) * _MainTex_TexelSize.xy;
            // Hard pixels: one source texel per world pixel, no filtering or colour blending.
            half4 texel = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, atlasUV, 0);
            clip(texel.a - _AlphaCutoff);
            // Binary source coverage, with ordinary material/renderer fade still supported.
            return half4(MixFog(texel.rgb * input.color.rgb, input.fogFactor), input.color.a);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _HINT_OUTLINE
            ENDHLSL
        }
        Pass
        {
            Name "SpriteUnlit"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _HINT_OUTLINE
            ENDHLSL
        }
    }
    Fallback Off
}
