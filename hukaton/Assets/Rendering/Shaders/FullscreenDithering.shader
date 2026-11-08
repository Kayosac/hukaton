Shader "Hidden/Hukaton/Fullscreen Dithering"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Dithering"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DitherParameters; // blend, threshold strength, levels - 1, dot size
                float4 _DitherPixelScale;
                int _DitherPattern;
                float _DitherMonochrome;
            CBUFFER_END

            float Threshold(uint2 cell)
            {
                if (_DitherPattern == 3)
                {
                    // Spatial hash only: no frame/time input, so a stationary image never flickers.
                    uint hash = cell.x * 374761393u + cell.y * 668265263u;
                    hash = (hash ^ (hash >> 13u)) * 1274126177u;
                    hash ^= hash >> 16u;
                    return ((hash & 65535u) + .5) / 65536.0;
                }
                uint rank = 0u;
                int order = _DitherPattern + 1;
                [unroll] for (int bit = 0; bit < 3; bit++)
                {
                    if (bit < order)
                    {
                        uint2 p = (cell >> bit) & 1u;
                        rank = rank * 4u + ((p.x ^ p.y) * 2u + p.y);
                    }
                }
                return (rank + .5) / (1u << (2 * order));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                if (_DitherParameters.x <= 0) return source;

                // Quantize in perceptual display space, then return to URP's working color space.
                float3 color = max(source.rgb, 0);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = LinearToSRGB(color);
                #endif
                if (_DitherMonochrome > .5) color = dot(color, float3(.2126, .7152, .0722)).xxx;
                float2 dotSize = max(_DitherParameters.w * _DitherPixelScale.xy, .01);
                uint2 cell = (uint2)floor(input.positionCS.xy / dotSize);
                float threshold = lerp(.5, Threshold(cell), _DitherParameters.y);
                float levels = _DitherParameters.z;
                float3 quantized = floor(saturate(color) * levels + threshold) / levels;
                // Preserve untonemapped highlights instead of clipping an HDR camera to white.
                quantized += max(color - 1, 0);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    quantized = SRGBToLinear(quantized);
                #endif
                return float4(lerp(source.rgb, quantized, _DitherParameters.x), source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
