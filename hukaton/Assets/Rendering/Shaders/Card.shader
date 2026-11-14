Shader "Hakaton/Card"
{
    Properties
    {
        [MainTexture] _MainTex ("Front", 2D) = "white" {}
        _BackTex ("Back", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1,1,1,1)

        [Header(Living Illustration)]
        [ToggleUI] _MotionEnabled ("Enable line animation", Float) = 1
        _AnimationFPS ("Drawing frames per second", Range(1,16)) = 8
        _Boil ("Line displacement (source pixels)", Range(0,12)) = 4
        _BoilScale ("Line variation scale", Range(4,60)) = 24
        _Seed ("Drawing seed", Float) = 0
        _MotionRect ("Illustration rect (X Y Width Height)", Vector) = (0.04,0.36,0.92,0.43)
        _MotionFeather ("Illustration edge feather", Range(0.001,0.1)) = 0.035
        [ToggleUI] _MotionOnBack ("Animate back", Float) = 0

        [Header(Surface From Sprite)]
        _InkProtection ("Preserve dark ink", Range(0,1)) = 1
        _ReliefStrength ("Emboss depth from sprite", Range(0,16)) = 7
        _ReliefRadius ("Emboss radius (source pixels)", Range(1,16)) = 5

        [Header(Card Back Always Hologram And Glow)]
        _BackEmissionStrength ("Back glow intensity", Range(0.01,12)) = 2.4
        _BackHologramStrength ("Back hologram coverage", Range(0.01,1)) = 0.85
        [HideInInspector] _FrontIsBack ("Face displays a card back", Float) = 0

        [Header(Emission)]
        [ToggleUI] _EmissionEnabled ("Enable glow", Float) = 0
        [HDR] _EmissionColor ("Glow colour", Color) = (1,1,1,1)
        _EmissionStrength ("Edge radiance", Range(0,12)) = 5
        _EmissionRadius ("Inward glow reach (card width)", Range(0.05,0.8)) = 0.48
        _EmissionPulse ("Inward wave energy", Range(0,1)) = 0.85
        _EmissionSpeed ("Inward wave speed", Range(0,4)) = 0.65

        [Header(Hologram)]
        [ToggleUI] _HologramEnabled ("Enable hologram", Float) = 0
        _HologramStrength ("Iridescent coating", Range(0,1)) = 0.85
        _HologramHueShift ("Accent hue shift limit (degrees)", Range(0,60)) = 12
        _HologramScale ("Diffraction band scale", Range(0.5,8)) = 1.8
        _HologramSpeed ("Iridescence speed", Range(0,2)) = 0.16

        [Header(Foil)]
        [ToggleUI] _FoilEnabled ("Enable foil", Float) = 0
        [HDR] _FoilColor ("Foil colour", Color) = (1,0.76,0.32,1)
        _FoilStrength ("Metal coverage", Range(0,1)) = 0.95
        _FoilSpeed ("Foil reflection speed", Range(0,2)) = 0.4

        [Header(Shine)]
        [ToggleUI] _ShineEnabled ("Enable shine", Float) = 0
        [HDR] _ShineColor ("Shine colour", Color) = (1,0.94,0.78,1)
        _ShineStrength ("Shine intensity", Range(0,5)) = 2.4
        _ShineWidth ("Shine width", Range(0.01,0.5)) = 0.28
        _ShineSpeed ("Shine sweeps per second", Range(0,2)) = 0.3
        _ShineAngle ("Shine angle", Range(-180,180)) = -30

        [HideInInspector] _LevelFont ("Upgrade font atlas", 2D) = "black" {}
        [HideInInspector] _LevelGlyphCount ("Upgrade glyph count", Float) = 0
        [HideInInspector] _LevelMarkColor ("Upgrade ink", Color) = (1,1,1,1)

        [Header(Gameplay Feedback)]
        _Flash ("Flash", Range(0,1)) = 0
        _FlashColor ("Flash colour", Color) = (1,1,1,1)
        _Glow ("Selection glow", Range(0,1)) = 0
        _GlowColor ("Selection glow colour", Color) = (1,0.85,0.4,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _Alpha ("Alpha", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        ZTest [_ZTest]
        ZWrite [_ZWrite]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Name "Card"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CardSurface.hlsl"

            TEXTURE2D(_LevelFont); SAMPLER(sampler_LevelFont);
            float _LevelGlyphCount;
            float4 _LevelGlyphUVs[16], _LevelGlyphRects[16];
            half4 _LevelMarkColor, _LevelPaperColor;
            float4 _LevelMarkRect;

            half4 LevelMark(float2 uv)
            {
                UNITY_BRANCH
                if (_LevelGlyphCount < 0.5) return 0;
                float2 paper = (uv - _LevelMarkRect.xy) / max(_LevelMarkRect.zw, 0.00001);
                if (any(paper < 0.0) || any(paper > 1.0)) return 0;
                half4 colour = _LevelPaperColor;
                for (int i = 0; i < (int)_LevelGlyphCount; i++)
                {
                    float4 rect = _LevelGlyphRects[i];
                    float2 p = (uv - rect.xy) / max(rect.zw, 0.00001);
                    if (all(p >= 0.0) && all(p < 1.0))
                    {
                        float4 glyph = _LevelGlyphUVs[i];
                        half alpha = SAMPLE_TEXTURE2D(_LevelFont, sampler_LevelFont, glyph.xy + p * glyph.zw).a;
                        colour.rgb = lerp(colour.rgb, _LevelMarkColor.rgb, alpha * _LevelMarkColor.a);
                        colour.a = max(colour.a, alpha * _LevelMarkColor.a);
                    }
                }
                return colour;
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewOS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // Extend the authored unit quad for the corner mark without scaling the card art.
                float2 uv = input.uv;
                if (_LevelGlyphCount > 0.5)
                {
                    float2 low = min(0.0, _LevelMarkRect.xy);
                    float2 high = max(1.0, _LevelMarkRect.xy + _LevelMarkRect.zw);
                    uv = lerp(low, high, input.uv);
                    input.positionOS.xy += uv - input.uv;
                }
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = uv;
                float3 viewWS = GetWorldSpaceViewDir(TransformObjectToWorld(input.positionOS));
                output.viewOS = TransformWorldToObjectDir(viewWS, true);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                bool front = IS_FRONT_VFACE(facing, true, false);
                half4 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                original.a *= all(input.uv >= 0.0) && all(input.uv <= 1.0);
                half4 mark = front && _FrontIsBack < 0.5 ? LevelMark(input.uv) : half4(0,0,0,0);
                // The mark may extend outside the physical card, on the front only.
                clip(max(original.a, mark.a) - _Cutoff);
                half3 colour = 0;
                if (original.a >= _Cutoff)
                {
                    float2 uv = front ? input.uv : float2(1.0 - input.uv.x, input.uv.y);
                    float2 texelSize = CardTexel(front ? _MainTex_TexelSize.xy : _BackTex_TexelSize.xy);
                    bool backStyle = !front || _FrontIsBack > 0.5;
                    float2 animatedUV = CardDrawingUV(uv, texelSize, backStyle ? _MotionOnBack : 1.0);
                    if (front)
                    {
                        half4 drawing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, animatedUV);
                        colour = drawing.a >= _Cutoff ? drawing.rgb : original.rgb;
                    }
                    else colour = SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, animatedUV).rgb;
                    float3 view = normalize(input.viewOS);
                    if (!front) view.x = -view.x;
                    colour = CardFinish(colour, uv, animatedUV, texelSize, view.xy, front, backStyle);
                }
                colour = lerp(colour, mark.rgb, mark.a);
                colour = CardFeedback(colour * _Tint.rgb, input.uv);
                return half4(colour, _Alpha * _Tint.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
