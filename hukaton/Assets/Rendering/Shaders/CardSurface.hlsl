#ifndef HAKATON_CARD_SURFACE_INCLUDED
#define HAKATON_CARD_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BackTex); SAMPLER(sampler_BackTex);
float4 _MainTex_TexelSize, _BackTex_TexelSize;

CBUFFER_START(UnityPerMaterial)
    half4 _Tint, _FlashColor, _GlowColor;
    half4 _EmissionColor, _FoilColor, _ShineColor;
    float4 _MotionRect;
    float _MotionEnabled, _AnimationFPS, _Boil, _BoilScale, _Seed, _MotionFeather, _MotionOnBack;
    float _InkProtection, _FinishOnBack, _ReliefStrength, _ReliefRadius;
    float _EmissionEnabled, _EmissionStrength, _EmissionPulse, _EmissionSpeed, _EmissionRadius;
    float _HologramEnabled, _HologramStrength, _HologramScale, _HologramSpeed, _HologramHueShift;
    float _FoilEnabled, _FoilStrength, _FoilSpeed;
    float _ShineEnabled, _ShineStrength, _ShineWidth, _ShineSpeed, _ShineAngle;
    half _Flash, _Glow, _Cutoff, _Alpha;
CBUFFER_END

float2 CardDrawingUV(float2 uv, float2 texelSize, float sideEnabled)
{
    UNITY_BRANCH
    if (_MotionEnabled < 0.5 || sideEnabled < 0.5 || _Boil <= 0.0) return uv;
    // Two coherent redraws, held for a full frame. No breathing, stretching or body sway.
    float frame = floor(_Time.y * max(_AnimationFPS, 1.0));
    float drawing = fmod(frame, 2.0);
    float phase = drawing * 2.173 + _Seed;
    float2 p = uv * _BoilScale;
    float2 offset = float2(sin(p.y + phase) + sin(p.x * 0.73 - phase * 1.3),
                          sin(p.x * 1.13 + phase * 0.9) + sin(p.y * 0.83 - phase));
    // Bottom-left UVs. Keep baked titles, descriptions and the physical card edge still.
    float2 inside = min(uv - _MotionRect.xy, _MotionRect.xy + _MotionRect.zw - uv);
    float weight = smoothstep(0.0, max(_MotionFeather, 0.001), min(inside.x, inside.y));
    float2 warped = uv + offset * 0.5 * texelSize * _Boil * weight;
    return clamp(warped, texelSize * 0.5, 1.0 - texelSize * 0.5);
}

// All finish masks and surface normals come from the existing face texture.
// No noise texture, normal map, user mask, or random foil cells are required.
half CardHeight(half3 rgb)
{
    return dot(rgb, half3(0.2126, 0.7152, 0.0722)) * 0.65 + max(rgb.r, max(rgb.g, rgb.b)) * 0.35;
}

half CardEnergy(half3 rgb)
{
    half high = max(rgb.r, max(rgb.g, rgb.b));
    half low = min(rgb.r, min(rgb.g, rgb.b));
    half saturation = (high - low) / max(high, 0.001);
    return smoothstep(0.18, 0.85, high) * lerp(0.15, 1.0, saturation);
}

half3 CardSampleSource(float2 uv, float2 texelSize, bool front)
{
    uv = clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
    if (front) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
    return SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, uv).rgb;
}

// Extract a stable light colour from the illustration, excluding the cream frame/captions.
half3 CardGlowPigment(half3 rgb)
{
    half high = max(rgb.r, max(rgb.g, rgb.b));
    half low = min(rgb.r, min(rgb.g, rgb.b));
    return rgb / max(high, 0.0001) * ((high - low) / max(high, 0.0001)) * smoothstep(0.04, 0.18, high);
}

half3 CardGlowPalette(float2 texelSize, bool front)
{
    half3 pigment = CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.18, 0.2), texelSize, front))
                  + CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.82, 0.2), texelSize, front))
                  + CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.12, 0.5), texelSize, front))
                  + CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.18, 0.8), texelSize, front))
                  + CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.82, 0.8), texelSize, front))
                  + CardGlowPigment(CardSampleSource(_MotionRect.xy + _MotionRect.zw * float2(0.88, 0.5), texelSize, front));
    half peak = max(pigment.r, max(pigment.g, pigment.b));
    return peak > 0.00001 ? pow(saturate(pigment / peak), 1.25) : half3(1, 1, 1);
}

half3 CardFinish(half3 source, float2 uv, float2 artUV, float2 texelSize, float2 view, bool front, float sideEnabled)
{
    UNITY_BRANCH
    if (sideEnabled < 0.5 || max(max(_EmissionEnabled, _HologramEnabled), max(_FoilEnabled, _ShineEnabled)) < 0.5)
        return source;

    half height = CardHeight(source);
    half luma = dot(source, half3(0.2126, 0.7152, 0.0722));
    // Protect actual black ink, not all the dark saturated colours of the illustration.
    half ink = lerp(1.0, smoothstep(0.008, 0.09, luma), _InkProtection);
    float2 inside = min(uv - _MotionRect.xy, _MotionRect.xy + _MotionRect.zw - uv);
    half illustration = smoothstep(0.0, max(_MotionFeather, 0.001), min(inside.x, inside.y));

    // Relief is derived from neighbouring source texels and follows the held drawing.
    // A screen-space lower bound prefilters small cards instead of aliasing fine paper texture.
    float2 pixelFootprint = max(abs(ddx(artUV)), abs(ddy(artUV)));
    float2 radius = max(texelSize * _ReliefRadius, pixelFootprint * 0.75);
    half3 left = CardSampleSource(artUV - float2(radius.x, 0), texelSize, front);
    half3 right = CardSampleSource(artUV + float2(radius.x, 0), texelSize, front);
    half3 down = CardSampleSource(artUV - float2(0, radius.y), texelSize, front);
    half3 up = CardSampleSource(artUV + float2(0, radius.y), texelSize, front);
    float2 gradient = float2(CardHeight(right) - CardHeight(left), CardHeight(up) - CardHeight(down));
    float3 normal = normalize(float3(-gradient * _ReliefStrength, 1.0));
    float3 eye = normalize(float3(view, sqrt(saturate(1.0 - dot(view, view))) + 0.001));
    half edge = saturate(length(gradient) * 3.0);
    float t = _Time.y;
    half3 colour = source;

    UNITY_BRANCH
    if (_FoilEnabled > 0.5)
    {
        // A moving studio reflection bends around embossed shapes already present in the print.
        float3 reflected = reflect(-eye, normal);
        float lighting = dot(reflected, float3(2.8, 3.6, 1.2)) + height * 2.4 + t * _FoilSpeed * 6.2831853;
        half reflection = smoothstep(-0.55, 0.75, sin(lighting));
        half highlight = pow(saturate(sin(lighting + 0.5)), 16.0);
        half metalLight = lerp(0.08, 1.45, reflection) + highlight * 0.9;
        // Keep pale caption fields bright enough to read while the picture gets deep metallic shadows.
        metalLight = max(metalLight, (1.0 - illustration) * 0.5);
        half3 metal = _FoilColor.rgb * metalLight * (0.55 + 0.45 * height) + source * 0.12;
        colour = lerp(colour, metal, saturate(_FoilStrength) * ink);
    }

    UNITY_BRANCH
    if (_HologramEnabled > 0.5 && _HologramStrength > 0.0)
    {
        // A coloured diffraction layer, not an achromatic moving light.
        // Printed values bend the bands slightly; fine paper normals do not randomise their hue.
        half3 hsv = RgbToHsv(LinearToSRGB(max(source, 0.0)));
        float phase = dot(uv, float2(0.95, 0.65)) * _HologramScale + height * 0.15
                    + dot(view, float2(0.9, -0.75)) + t * _HologramSpeed;
        float bandDistance = abs(frac(phase) - 0.5);
        half diffraction = 1.0 - smoothstep(0.06, 0.27, bandDistance);
        float spectralPhase = phase * 0.5 + dot(uv, float2(-0.35, 1.1))
                            + dot(view, float2(0.2, 0.55)) + t * _HologramSpeed * 0.5;
        // A continuous hue excursion avoids the wrap seam of shortest-arc hue blending.
        float hueDelta = sin(spectralPhase * 6.2831853) * 0.5;
        // Dark saturated backgrounds are coloured pigment, not black contour ink.
        half colouredPigment = smoothstep(0.35, 0.65, hsv.y) * smoothstep(0.18, 0.50, hsv.z);
        half hologramInk = max(ink, lerp(1.0, colouredPigment, _InkProtection));
        half coating = saturate(_HologramStrength) * diffraction * illustration * hologramInk;
        // Only the bright coloured focal pigment is protected. The surrounding print can
        // show a full spectrum, so preserving the accent does not erase the hologram.
        half3 localPigment = LinearToSRGB(max(source, max(max(left, right), max(up, down))));
        half pigmentValue = max(localPigment.r, max(localPigment.g, localPigment.b));
        half accent = smoothstep(0.55, 0.80, pigmentValue) * smoothstep(0.06, 0.25, hsv.y);
        float hueLimit = lerp(0.5, _HologramHueShift / 360.0, accent);
        half3 filmHSV = hsv;
        filmHSV.x = frac(hsv.x + clamp(hueDelta * coating, -hueLimit, hueLimit));
        filmHSV.y = lerp(hsv.y, max(hsv.y, 0.90), coating * 0.75);
        filmHSV.z = lerp(hsv.z, max(hsv.z, 0.72), coating * 0.75);
        // Mix hue in HSV: complementary pigments must not average into grey/brown mud.
        half3 film = SRGBToLinear(HsvToRgb(filmHSV));
        colour = _FoilEnabled > 0.5 ? lerp(colour, film, coating * 0.55) : film;
    }

    UNITY_BRANCH
    if (_ShineEnabled > 0.5)
    {
        float angle = radians(_ShineAngle);
        float2 direction = float2(cos(angle), sin(angle));
        float extent = dot(abs(direction), float2(0.5, 0.5));
        float width = max(_ShineWidth, 0.001);
        float centre = lerp(-extent - width, extent + width, frac(t * _ShineSpeed + 0.15));
        float relief = dot(normal.xy, direction) * 0.12 + (height - 0.5) * 0.1;
        float distance = abs(dot(uv - 0.5, direction) + dot(view, direction) * 0.15 + relief - centre);
        half sweep = 1.0 - smoothstep(0.0, width, distance);
        float3 lamp = normalize(float3(direction * 0.6, 0.8));
        float3 halfway = normalize(lamp + eye);
        half specular = pow(saturate(dot(normal, halfway)), 40.0);
        half gloss = specular * 0.25 + sweep * sweep * (0.45 + 0.55 * height + edge * 0.5);
        colour *= 1.0 - 0.2 * saturate(_ShineStrength) * ink;
        colour += _ShineColor.rgb * _ShineStrength * gloss * ink;
    }

    UNITY_BRANCH
    if (_EmissionEnabled > 0.5 && _EmissionStrength > 0.0)
    {
        // Rounded physical card edge; distance is measured in card-width units.
        float aspect = clamp(texelSize.x / max(texelSize.y, 0.000001), 0.5, 3.0);
        float2 centred = (uv - 0.5) * float2(1.0, aspect);
        float corner = 0.075;
        float2 cornerDelta = abs(centred) - (float2(0.5, aspect * 0.5) - corner);
        float depth = max(0.0, -(length(max(cornerDelta, 0.0)) + min(max(cornerDelta.x, cornerDelta.y), 0.0) - corner));
        float inward = depth * 2.0;
        float angle = atan2(centred.y, centred.x + 0.000001);
        float clock = t * _EmissionSpeed;
        // Increasing time moves a phase crest to greater depth: every wave travels INWARD.
        float bend = 1.0 - smoothstep(0.6, 1.0, inward);
        float flow = inward * 2.0 - clock + 0.12 * bend * sin(angle * 5.0 + clock * 6.2831853);
        half wave = pow(0.5 + 0.5 * cos(flow * 6.2831853), 5.0);
        half hotSpot = 0.5 + 0.5 * sin(angle * 3.0 - clock * 6.2831853);
        half rim = exp2(-depth / 0.022);
        half falloff = exp2(-depth / max(_EmissionRadius, 0.01) * 1.5);
        half energy = rim * (1.0 + _EmissionPulse * hotSpot * 0.6)
                    + falloff * (0.10 + _EmissionPulse * wave * 0.9);
        half3 palette = CardGlowPalette(texelSize, front);
        half3 radiance = palette * energy + pow(rim, 4.0) * 0.16;
        // The outer frame emits too, while the interiors of the title/caption panels stay readable.
        half frame = 1.0 - smoothstep(0.06, 0.10, depth);
        half area = max(illustration, frame);
        half sourcePeak = max(source.r, max(source.g, source.b));
        half glowInk = lerp(1.0, smoothstep(0.04, 0.16, sourcePeak) * smoothstep(0.012, 0.045, luma), _InkProtection);
        colour += _EmissionColor.rgb * _EmissionStrength * radiance * area * glowInk;
    }
    return colour;
}

half3 CardFeedback(half3 colour, float2 uv)
{
    // Keep selection/return-card feedback separate from decorative emission.
    float2 centred = abs(uv - 0.5) * 2.0;
    half edge = saturate(max(centred.x, centred.y) * 1.25 - 0.35);
    colour += _GlowColor.rgb * (0.25 + edge * 0.6) * _Glow;
    return lerp(colour, _FlashColor.rgb, _Flash);
}
#endif
