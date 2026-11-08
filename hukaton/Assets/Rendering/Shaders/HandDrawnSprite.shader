Shader "Hukaton/Sprites/Hand Drawn Pixel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Header(Crisp Pixels)]
        _PixelSize ("Pixel Size (source texels)", Range(1,32)) = 8
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
        // Explicit point sampling at mip zero: sharp even on bilinear source textures.
        // sampler_PointClamp is supplied by URP's GlobalSamplers.hlsl.
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _PixelSize, _AlphaCutoff, _Motion, _AnimationFPS;
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
            output.color = input.color * _Color * unity_SpriteColor;
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 cell = max(1.0, floor(_PixelSize + 0.5)) * _MainTex_TexelSize.xy;
            // Quantize BEFORE animation: deformation cannot bend or blur the pixel grid.
            float2 uv = (floor(input.uv / cell) + 0.5) * cell;
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
            half4 texel = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, uv, 0);
            clip(texel.a - _AlphaCutoff);
            // Binary source coverage, with ordinary material/renderer fade still supported.
            return half4(texel.rgb * input.color.rgb, input.color.a);
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
            ENDHLSL
        }
    }
    Fallback Off
}
