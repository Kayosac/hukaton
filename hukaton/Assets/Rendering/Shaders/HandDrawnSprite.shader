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
        // Explicit-LOD sampling with URP's global sampler_LinearClamp (GlobalSamplers.hlsl).
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
            float fogFactor : TEXCOORD2;
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

            // Reject outside the source; clamping alone would smear opaque edge texels.
            clip(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)));
            // ONE read per world pixel from the mip level whose texels match the world pixel: every
            // creature, NPC and item shows the same averaged drawing per world pixel, whatever its
            // source resolution or scale. Sources coarser than the grid are interpolated at level 0,
            // so their texels never show as bigger blocks. Needs mipmaps on the sprite texture.
            float2 footprint = cell * rectSize * _MainTex_TexelSize.zw;
            float lod = max(0.0, log2(max(footprint.x, footprint.y)));
            // Keep sampling inside this cell even at an exact edge (never the neighbouring pose).
            float2 inset = _MainTex_TexelSize.xy * 0.5;
            float2 atlasUV = clamp(_SpriteUVRect.xy + uv * rectSize,
                _SpriteUVRect.xy + inset, _SpriteUVRect.xy + rectSize - inset);
            half4 texel = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearClamp, atlasUV, lod);
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
            ENDHLSL
        }
    }
    Fallback Off
}
