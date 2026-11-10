// The PC's picture on a full-screen UI image, as an old bulging CRT (scanlines, glow, colour fringing, power-on line).
// The content mapping (Centred, ContentUV) must stay identical to CrtScreenMath.ContentUV in C#: mouse picking uses it.
Shader "Hakaton/Computer/CRT Screen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Screen content", 2D) = "black" {}
        _QuadAspect ("Glass aspect (width / height)", Float) = 1.333
        _ContentAspect ("Content aspect", Float) = 1.333
        _Fill ("Content height on the glass", Range(.5, 1.2)) = .97
        [HideInInspector] _ArtScale ("Texture pixels per canvas pixel (set by CrtScreen)", Float) = 1
        _Curve ("Bulge", Range(0, .4)) = .08
        _Corner ("Corner rounding", Range(0, .2)) = .05
        _Scan ("Scanlines", Range(0, 1)) = .5
        _Sharpness ("Texel edge sharpness", Range(1, 4)) = 2
        _Mask ("Shadow mask", Range(0, 1)) = .05
        _Vignette ("Vignette", Range(0, 1)) = .4
        _Glow ("Phosphor glow", Range(0, 2)) = .15
        _Aberration ("Colour fringing (texels)", Range(0, 2)) = .15
        _Brightness ("Brightness", Range(0, 4)) = 1.1
        _GlassColor ("Glass when dark", Color) = (0, 0, 0, 1)
        _Reflection ("Glass reflection", Range(0, 1)) = 0
        [Header(Power)]
        _OpenX ("Open width", Range(0, 1)) = 1
        _OpenY ("Open height", Range(0, 1)) = 1
        _Dot ("Afterglow dot", Range(0, 1)) = 0
        _Wobble ("Degauss wobble", Range(0, 1)) = 0
        _Noise ("Static", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
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
            float _QuadAspect, _ContentAspect, _Fill, _ArtScale, _Curve, _Corner, _Scan, _Sharpness, _Mask, _Vignette, _Glow, _Aberration, _Brightness;
            float4 _GlassColor;
            float _Reflection, _OpenX, _OpenY, _Dot, _Wobble, _Noise, _Flash;

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

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // Glass position (0..1 over the image) -> centred picture coordinates, -1..1 inside the picture.
            float2 Centred(float2 glass)
            {
                float2 c = glass - .5;
                c.x *= _QuadAspect / _ContentAspect;
                return c * 2 / _Fill;
            }

            float2 ContentUV(float2 centred)
            {
                float2 d = centred;
                d *= 1 + _Curve * dot(d, d);
                return d * .5 + .5;
            }

            float3 Sample(float2 uv, float2 scale)
            {
                // Sharp bilinear: crisp texels, smooth only across texel borders.
                float2 size = _MainTex_TexelSize.zw;
                float2 texel = uv * size;
                float2 cell = floor(texel);
                // Sharpness > 1: the blend between neighbouring texels is narrower than a screen pixel.
                float2 f = saturate((frac(texel) - .5) * scale * _Sharpness + .5);
                return tex2Dlod(_MainTex, float4((cell + f) / size, 0, 0)).rgb;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 glass = input.uv;
                float2 c = Centred(glass);
                float t = _Time.y;

                // Degauss: the picture rings sideways for a moment.
                c.x += _Wobble * .05 * sin(c.y * 9 + t * 38) * (1 - abs(c.y) * .5);
                float2 uv = ContentUV(c);

                float3 glassCol = _GlassColor.rgb;
                glassCol += _Reflection * smoothstep(.65, .1, length((glass - float2(.22, .78)) * float2(1, 1.6)));

                // ---- power: the picture squeezed into the tube's open height, with soft, glowing edges ----
                float openY = max(_OpenY, .001);
                float2 suv = float2(uv.x, .5 + (uv.y - .5) / openY);
                float2 e = abs(suv - .5) - (.5 - _Corner);
                float cornerDist = length(max(e, 0)) - _Corner;
                float pictureMask = saturate(-cornerDist * 300) * step(0, suv.x) * step(suv.x, 1) * step(0, suv.y) * step(suv.y, 1);
                float2 px = max(fwidth(c), 1e-4);
                float edgeY = smoothstep(openY + px.y, openY - px.y, abs(c.y));
                float edgeX = smoothstep(_OpenX + px.x, _OpenX - px.x, abs(c.x));
                float pictureWeight = smoothstep(.012, .05, _OpenY);
                float inside = pictureMask * edgeX * edgeY * pictureWeight;

                // Branch-free: derivatives stay defined everywhere.
                float2 scale = max(1, 1 / max(fwidth(suv * _MainTex_TexelSize.zw), 1e-4));
                // Scanlines, fringing, glow and static follow the canvas' own pixels, not the supersampled texture's.
                float artScale = max(_ArtScale, 1);
                float2 artSize = _MainTex_TexelSize.zw / artScale;
                float rowPos = suv.y * artSize.y;
                float pixelsPerRow = 1 / max(fwidth(rowPos), 1e-4);

                float2 off = (suv - .5) * _Aberration * 2 / artSize;
                float3 col;
                col.r = Sample(suv + off, scale).r;
                col.g = Sample(suv, scale).g;
                col.b = Sample(suv - off, scale).b;
                float3 glow = tex2Dlod(_MainTex, float4(suv, 0, 3 + log2(artScale))).rgb;
                col += glow * glow * _Glow;

                // Scanlines: each texel row is a bright beam with a dark gap below it.
                float rowOffset = frac(rowPos) - .5;
                float beam = exp(-rowOffset * rowOffset * 9);
                col *= lerp(1, beam * 1.3, _Scan * saturate(pixelsPerRow - 1));
                float column = frac(input.pos.x / 3);
                float3 mask = column < .333 ? float3(1, .82, .82) : column < .666 ? float3(.82, 1, .82) : float3(.82, .82, 1);
                col *= lerp(1, mask * 1.12, _Mask);

                float n = Hash(floor(suv * artSize) + frac(t * 17.13) * 91.7);
                col = lerp(col, n.xxx * .9, _Noise);
                // The same light squeezed into less height burns brighter and whiter.
                float squeeze = 1 - saturate(_OpenY);
                col = col * (1 + 5 * pow(squeeze, 5)) + _Flash;
                col = lerp(col, float3(.9, .96, 1) * 1.6, pow(squeeze, 9) * .85);
                col *= 1 + .012 * sin(t * 377);
                float2 v = suv - .5;
                col *= 1 - _Vignette * dot(v, v) * 2.2;
                col *= _Brightness;

                // Physical units (equal in x and y) for the beam and the dot.
                float2 p = float2(c.x * _ContentAspect, c.y);
                float3 phosphor = float3(.82, .93, 1);

                // The rim of the opening picture glows while it travels.
                // Following the picture's own (bulged) top and bottom edges, only where the picture is.
                float edgeDistance = (.5 - abs(suv.y - .5)) * 2 * openY;
                float rimMask = step(0, suv.x) * step(suv.x, 1) * saturate(-(length(max(float2(abs(suv.x - .5) - (.5 - _Corner), 0), 0)) - _Corner) * 300);
                float rim = exp(-pow(edgeDistance / .012, 2)) * step(-.001, edgeDistance) * rimMask * edgeX * squeeze * pictureWeight * 1.4;

                // The beam: a lens-shaped line, thickest in the middle, with a soft halo.
                float halfLength = max(_OpenX * _ContentAspect, 1e-3);
                float taper = saturate(1 - pow(p.x / halfLength, 2));
                float thick = max(_OpenY, .005) * sqrt(taper) + .0012;
                float core = exp(-pow(p.y / thick, 2));
                float halo = exp(-abs(p.y) / (thick * 6 + .012)) * .45;
                float beamWeight = (1 - pictureWeight) * step(.0001, _OpenX) * pow(taper, .35);
                float3 beamColour = phosphor * (core * 2.6 + halo) * beamWeight;

                // The dot: a hot core, a halo and a faint cross flare, flickering as it dies.
                float r = length(p);
                float flicker = .9 + .1 * sin(t * 97);
                float spot = exp(-pow(r / .012, 2)) * 4 + exp(-r / .045) * .7
                          + exp(-abs(p.y) / .0025) * exp(-abs(p.x) / .12) * .5
                          + exp(-abs(p.x) / .0025) * exp(-abs(p.y) / .07) * .25;
                float3 dotColour = phosphor * spot * _Dot * flicker;

                float3 result = lerp(glassCol, col, inside) + phosphor * rim + beamColour + dotColour;
                return fixed4(result, input.color.a);
            }
            ENDCG
        }
    }
}
