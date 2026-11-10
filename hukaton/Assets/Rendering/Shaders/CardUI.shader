Shader "Hakaton/Card UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card art", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1,1,1,1)

        [Header(Living Illustration)]
        [ToggleUI] _MotionEnabled ("Enable line animation", Float) = 1
        _AnimationFPS ("Drawing frames per second", Range(1,16)) = 8
        _Boil ("Line displacement (source pixels)", Range(0,12)) = 4
        _BoilScale ("Line variation scale", Range(4,60)) = 24
        _Seed ("Drawing seed", Float) = 0
        _MotionRect ("Illustration rect (X Y Width Height)", Vector) = (0.04,0.36,0.92,0.43)
        _MotionFeather ("Illustration edge feather", Range(0.001,0.1)) = 0.035

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

        [Header(Gameplay Feedback)]
        _Flash ("Flash", Range(0,1)) = 0
        _FlashColor ("Flash colour", Color) = (1,1,1,1)
        _Glow ("Selection glow", Range(0,1)) = 0
        _GlowColor ("Selection glow colour", Color) = (1,0.85,0.4,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _Alpha ("Alpha", Range(0,1)) = 1
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"
               "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "CardUI"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CardSurface.hlsl"

            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Tint;
                float2 pixelSize = output.positionCS.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.positionOS.xy * 2.0 - rect.xy - rect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                bool backStyle = _FrontIsBack > 0.5;
                float2 uv = CardDrawingUV(input.uv, _MainTex_TexelSize.xy, backStyle ? 0.0 : 1.0);
                half4 drawing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half3 art = drawing.a >= _Cutoff ? drawing.rgb : original.rgb;
                // Overlay UI has no physical tilt. Coatings still animate at their own speeds.
                half3 colour = CardFinish(art, input.uv, uv, _MainTex_TexelSize.xy, float2(0,0), true, backStyle) * input.color.rgb;
                colour = CardFeedback(colour, input.uv);
                half alpha = original.a * input.color.a * _Alpha;
                #ifdef UNITY_UI_CLIP_RECT
                    half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    alpha *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(alpha - 0.001);
                #endif
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
