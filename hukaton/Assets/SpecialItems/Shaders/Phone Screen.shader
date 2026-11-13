// The phone's display on its glass (a RawImage of the display's render texture): a small backlit screen with a barely
// bulging glass, strong scan lines tied to the picture (the same count at any resolution), an RGB mask, the glow of
// the bright parts bleeding out, a glare band and a hot spot on the glass, a darker rim under the bezel and a faint
// flicker. Made for the phone; the PC keeps its own tube (Computer/Shaders/CrtScreen.shader).
Shader "Hakaton/Special Items/Phone Screen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Display", 2D) = "black" {}
        [Header(Glass)]
        _Curve ("Bulge", Range(0, .2)) = .035
        _Corner ("Corner rounding", Range(0, .2)) = .05
        _Rim ("Rim shadow under the bezel", Range(0, 1)) = .55
        _Vignette ("Vignette", Range(0, 1)) = .45
        [Header(Picture)]
        _Sharpness ("Texel edge sharpness", Range(1, 4)) = 2.5
        _Brightness ("Brightness", Range(0, 3)) = 1.2
        _Lines ("Scan lines over the picture height", Float) = 102
        _Scan ("Scan line depth", Range(0, 1)) = .6
        _Mask ("RGB mask", Range(0, 1)) = .25
        _MaskPixels ("RGB mask stripe, screen pixels", Range(1, 6)) = 1
        _Aberration ("Colour fringing (texels)", Range(0, 2)) = .35
        [Header(Light)]
        _Glow ("Glow of bright parts", Range(0, 3)) = 1.1
        _GlowMip ("Glow spread (mip)", Range(0, 5)) = 2.5
        _Glare ("Glare band on the glass", Range(0, 1)) = .22
        _Hotspot ("Hot spot on the glass", Range(0, 1)) = .3
        _Flicker ("Flicker", Range(0, .1)) = .025
        _GlassColor ("Glass when dark", Color) = (.015, .03, .025, 1)
        // Written by CrtScreen (shared component with the PC); unused here.
        [HideInInspector] _QuadAspect ("", Float) = 1
        [HideInInspector] _ContentAspect ("", Float) = 1
        [HideInInspector] _Fill ("", Float) = 1
        [HideInInspector] _ArtScale ("", Float) = 1
        [HideInInspector] _OpenX ("", Float) = 1
        [HideInInspector] _OpenY ("", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Curve, _Corner, _Rim, _Vignette, _Sharpness, _Brightness, _Lines, _Scan, _Mask, _MaskPixels, _Aberration;
            float _Glow, _GlowMip, _Glare, _Hotspot, _Flicker, _OpenY;
            float4 _GlassColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // Sharp bilinear: whole texels, smooth only across their borders.
            float3 Sample(float2 uv, float2 scale)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 texel = uv * size;
                float2 cell = floor(texel);
                float2 f = saturate((frac(texel) - .5) * scale * _Sharpness + .5);
                return tex2Dlod(_MainTex, float4((cell + f) / size, 0, 0)).rgb;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float t = _Time.y;
                // Barely bulging glass.
                float2 c = input.uv * 2 - 1;
                c *= 1 + _Curve * dot(c, c);
                float2 uv = c * .5 + .5;
                float2 e = abs(uv - .5) - (.5 - _Corner);
                float corner = length(max(e, 0)) - _Corner;
                float2 px = max(fwidth(uv), 1e-5);
                float inside = saturate(-corner / max(px.x, px.y));

                float2 scale = max(1, 1 / max(fwidth(uv * _MainTex_TexelSize.zw), 1e-4));
                float2 off = (uv - .5) * _Aberration * 2 * _MainTex_TexelSize.xy;
                float3 col;
                col.r = Sample(uv + off, scale).r;
                col.g = Sample(uv, scale).g;
                col.b = Sample(uv - off, scale).b;

                // Glow: the blurred picture (mips) bleeds its bright parts out, past the texel edges.
                float3 glow = tex2Dlod(_MainTex, float4(uv, 0, _GlowMip)).rgb;
                float3 wide = tex2Dlod(_MainTex, float4(uv, 0, _GlowMip + 1.5)).rgb;
                col += (glow * glow * .7 + wide * wide * .5) * _Glow;

                // Scan lines tied to the picture: the same number at any resolution.
                float row = uv.y * _Lines;
                float beam = exp(-pow(frac(row) - .5, 2) * 10);
                float rowsOnScreen = 1 / max(fwidth(row), 1e-4);
                col *= lerp(1, beam * 1.45, _Scan * saturate(rowsOnScreen * .5));
                // RGB stripes on the screen's own pixels.
                float column = frac(input.pos.x / (3 * _MaskPixels));
                float3 mask = column < .333 ? float3(1, .7, .7) : column < .666 ? float3(.7, 1, .7) : float3(.7, .7, 1);
                col *= lerp(1, mask * 1.2, _Mask);

                // Light: vignette, the rim under the bezel, flicker.
                float2 v = uv - .5;
                col *= 1 - _Vignette * dot(v, v) * 2.4;
                float edge = min(min(uv.x, 1 - uv.x), min(uv.y, 1 - uv.y));
                col *= 1 - _Rim * (1 - smoothstep(0, .07, edge));
                col *= _Brightness * (1 + _Flicker * sin(t * 61) * sin(t * 7.3));

                // Glass: a soft diagonal glare band and a hot spot near the top left, over everything.
                float band = dot(input.uv - float2(.15, .85), normalize(float2(1, 1)));
                float glare = exp(-pow((band - .05) / .09, 2)) * .8 + exp(-pow((band + .22) / .03, 2)) * .35;
                float spot = exp(-dot((input.uv - float2(.22, .82)) * float2(1, 1.15), (input.uv - float2(.22, .82)) * float2(1, 1.15)) / .012);
                float3 glass = float3(.9, .97, 1) * (glare * _Glare + spot * _Hotspot);

                float3 result = lerp(_GlassColor.rgb, col, inside * saturate(_OpenY)) + glass * inside;
                return fixed4(result, input.color.a);
            }
            ENDCG
        }
    }
}
