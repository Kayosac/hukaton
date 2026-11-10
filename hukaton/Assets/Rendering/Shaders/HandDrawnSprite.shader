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
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        // Explicit mip-zero sampling with URP's global sampler_LinearClamp/PointClamp (GlobalSamplers.hlsl):
        // the shader filters each world pixel itself, so texture import filtering/mips do not matter.
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float4 _SpriteUVRect;
            float _PixelsPerUnit, _AlphaCutoff, _Motion, _AnimationFPS;
            float _Speed, _Sway, _Breath, _Boil, _Anchor, _Seed;
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
            output.positionRelativeWS = TransformObjectToWorld(input.positionOS) - viewOriginWS;
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
            float2 cell = clamp(determinant / max(axisLengths * max(_PixelsPerUnit, 0.001), 1e-12), 1e-7, 1.0);
            // A whole number of cells across the drawing. Derivatives differ in their last bits
            // between 2x2 pixel quads; the rounded count is identical for every fragment of one
            // sprite, so the grid never frays. The density error is at most half a cell per sprite.
            return 1.0 / max(floor(1.0 / cell + 0.5), 1.0);
        }

        // Taps per axis inside one world pixel. Bilinear taps two texels apart cover the cell
        // footprint exactly up to 2 * MAX_CELL_TAPS source texels per world pixel.
        #define MAX_CELL_TAPS 6

        // Bilinear sample weighted by alpha. Hardware filtering is used inside the drawing; where
        // it meets transparency the four texels are premultiplied individually, so the colour of
        // invisible texels never bleeds into the outline (no halos, whatever the PNG stores there).
        float4 PremultipliedBilinear(float2 atlasUV, float2 low, float2 high)
        {
            half4 filtered = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearClamp, atlasUV, 0);
            if (filtered.a <= 0.002 || filtered.a >= 0.998) return float4(filtered.rgb * filtered.a, filtered.a);
            float2 texelPosition = atlasUV * _MainTex_TexelSize.zw - 0.5;
            float2 f = frac(texelPosition);
            float2 origin = (floor(texelPosition) + 0.5) * _MainTex_TexelSize.xy;
            float2 step = _MainTex_TexelSize.xy;
            half4 t00 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, clamp(origin, low, high), 0);
            half4 t10 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, clamp(origin + float2(step.x, 0), low, high), 0);
            half4 t01 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, clamp(origin + float2(0, step.y), low, high), 0);
            half4 t11 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, clamp(origin + step, low, high), 0);
            float4 bottom = lerp(float4(t00.rgb * t00.a, t00.a), float4(t10.rgb * t10.a, t10.a), f.x);
            float4 top = lerp(float4(t01.rgb * t01.a, t01.a), float4(t11.rgb * t11.a, t11.a), f.x);
            return lerp(bottom, top, f.y);
        }

        // Average (alpha-weighted) of the drawing under one world pixel, centred at uv (sprite-local).
        // Dense sources are box-filtered, so thin lines survive identically whatever the source
        // resolution; coarse sources are interpolated, so their texels never show as bigger blocks.
        float4 CellCoverage(float2 uv, float2 cell, float2 rectSize)
        {
            float2 footprint = cell * rectSize * _MainTex_TexelSize.zw; // source texels per world pixel
            int2 taps = (int2)clamp(ceil(footprint * 0.5), 1.0, (float)MAX_CELL_TAPS);
            // Keep every bilinear tap inside this sprite's atlas cell (never the neighbouring pose).
            float2 inset = _MainTex_TexelSize.xy * 0.5;
            float2 low = _SpriteUVRect.xy + inset, high = _SpriteUVRect.xy + rectSize - inset;
            float4 sum = 0;
            [loop] for (int y = 0; y < MAX_CELL_TAPS; y++)
            {
                if (y >= taps.y) break;
                [loop] for (int x = 0; x < MAX_CELL_TAPS; x++)
                {
                    if (x >= taps.x) break;
                    float2 p = uv + ((float2(x, y) + 0.5) / float2(taps) - 0.5) * cell;
                    // Outside the drawing counts as empty, not as a smeared edge texel.
                    if (any(p < 0.0) || any(p > 1.0)) continue;
                    float2 atlasUV = clamp(_SpriteUVRect.xy + p * rectSize, low, high);
                    sum += PremultipliedBilinear(atlasUV, low, high);
                }
            }
            return sum / float(taps.x * taps.y);
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            // Sprite sheets share one texture: animate inside this sprite's cell, around its own feet.
            float2 rectSize = max(_SpriteUVRect.zw, float2(0.00001, 0.00001));
            // Quantize BEFORE animation: deformation cannot bend or blur the pixel grid.
            float2 localUV = (input.uv - _SpriteUVRect.xy) / rectSize;
            float2 cell = WorldPixelCell(localUV, input.positionRelativeWS);
            float2 uv = (floor(localUV / cell) + 0.5) * cell;
            float t = floor(_Time.y * max(_AnimationFPS, 1.0)) / max(_AnimationFPS, 1.0);
            float phase = t * _Speed * 2.0 + _Seed;
            float height = saturate((uv.y - _Anchor) / max(1.0 - _Anchor, 0.01));
            float weight = height * height;

            // Inverse warp around the feet. Full Rect sprites keep the moving outline intact.
            uv.y = _Anchor + (uv.y - _Anchor) / (1.0 + sin(phase) * _Breath * _Motion);
            uv.x -= sin(phase * 0.73) * _Sway * cell.x * weight * _Motion;
            uv.x = 0.5 + (uv.x - 0.5) / (1.0 - sin(phase) * _Breath * 0.4 * _Motion * height);

            // Low-frequency coherent wobble, held for each drawing frame (no random flicker).
            float2 wobble = float2(
                sin(uv.y * 23.0 + phase * 1.71) + sin(uv.x * 17.0 - phase * 1.13),
                sin(uv.x * 21.0 + phase * 1.37) + sin(uv.y * 13.0 - phase * 1.91));
            uv += wobble * 0.5 * cell * _Boil * height * _Motion;

            // One colour per world pixel, independent of the source resolution and import size.
            float4 coverage = CellCoverage(uv, cell, rectSize);
            clip(coverage.a - _AlphaCutoff);
            half3 colour = coverage.rgb / max(coverage.a, 1e-5);
            // Binary coverage, with ordinary material/renderer fade still supported.
            return half4(colour * input.color.rgb, input.color.a);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "SpriteUnlit"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
