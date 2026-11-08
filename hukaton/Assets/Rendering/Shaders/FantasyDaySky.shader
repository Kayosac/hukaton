Shader "Hukaton/Fantasy Day Sky"
{
    Properties
    {
        [Header(Atmosphere)]
        _ZenithColor("Zenith / cornflower", Color) = (0.24,0.46,0.72,1)
        _SkyColor("Sky / turquoise blue", Color) = (0.43,0.68,0.79,1)
        _HorizonColor("Horizon / warm haze", Color) = (0.88,0.85,0.70,1)
        _GroundColor("Below horizon", Color) = (0.46,0.53,0.48,1)
        _Exposure("Exposure", Range(0,3)) = 1
        _PixelResolution("Vertical sky pixels", Range(96,1024)) = 360
        _Seed("Cloud seed", Float) = 108
        [Header(Clouds)]
        _CloudLight("Sunlit cream", Color) = (1,0.96,0.82,1)
        _CloudShadow("Lavender shade", Color) = (0.55,0.59,0.73,1)
        _CloudCoverage("Coverage", Range(0,1)) = 0.52
        _CloudScale("Cloud scale", Range(1,12)) = 4.5
        _CloudSoftness("Edge softness", Range(0.01,0.2)) = 0.065
        _CloudSteps("Cloud shading bands", Range(2,12)) = 5
        _Wind("Wind XYZ and speed", Vector) = (1,0,0.35,0.015)
        [Header(Sun follows the scene Directional Light)]
        _SunTint("Sun tint", Color) = (1,0.89,0.58,1)
        _SunRadius("Sun angular radius in degrees", Range(0.2,10)) = 3.5
        _SunHalo("Halo strength", Range(0,1)) = 0.24
        _SunEmber("Sun amber surface", Color) = (1,0.48,0.16,1)
        _SunCorona("Living corona", Range(0,2)) = 0.65
        _SunDetail("Surface detail", Range(0,1)) = 0.8
        _SunSpeed("Solar animation speed", Range(0,2)) = 0.3
        [Header(Distant daytime heavens)]
        _GalaxyColor("Galaxy pearl and lilac", Color) = (0.88,0.81,1,1)
        _GalaxyVisibility("Galaxy visibility through atmosphere", Range(0,0.5)) = 0.16
        _GalaxyOne("Galaxy 1 yaw elevation radius roll", Vector) = (176,39,23,-28)
        _GalaxyTwo("Galaxy 2 yaw elevation radius roll", Vector) = (48,58,19,35)
        _StarVisibility("Daytime starlight", Range(0,1)) = 0.32
        _StarDensity("Star density", Range(0,1)) = 0.35
        [Header(Day moons)]
        _MoonColor("Moon ivory", Color) = (0.93,0.94,0.81,1)
        _MoonOpacity("Moon visibility", Range(0,1)) = 0.38
        _MoonYaw("Moon azimuth in degrees", Range(-180,180)) = 115
        _MoonElevation("Moon elevation in degrees", Range(0,85)) = 32
        _MoonRadius("Moon angular radius in degrees", Range(0.5,15)) = 6
        _MoonPhase("First moon phase offset", Range(-1,1)) = 0.12
        _MoonTwo("Moon 2 yaw elevation radius phase", Vector) = (163,20,3.2,0.55)
        _MoonThree("Moon 3 yaw elevation radius phase", Vector) = (-65,48,4.5,0.25)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _SkyColor, _HorizonColor, _GroundColor;
                float4 _CloudLight, _CloudShadow, _Wind, _SunTint, _MoonColor;
                float4 _SunEmber, _GalaxyColor, _GalaxyOne, _GalaxyTwo, _MoonTwo, _MoonThree;
                float _Exposure, _PixelResolution, _Seed;
                float _CloudCoverage, _CloudScale, _CloudSoftness, _CloudSteps;
                float _SunRadius, _SunHalo, _MoonOpacity, _MoonYaw, _MoonElevation, _MoonRadius;
                float _SunCorona, _SunDetail, _SunSpeed, _GalaxyVisibility, _StarVisibility, _StarDensity, _MoonPhase;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // As in CatWalk: translation-free direction, projected at the far plane.
                float3 view = mul((float3x3)UNITY_MATRIX_V, input.positionOS.xyz);
                output.positionCS = mul(UNITY_MATRIX_P, float4(view, 1));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = 0;
                #else
                    output.positionCS.z = output.positionCS.w;
                #endif
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(
                    lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x),
                         lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x),
                         lerp(Hash(cell + float3(0,1,1)), Hash(cell + 1), f.x), f.y), f.z);
            }

            float Fractal(float3 p)
            {
                float value = Noise(p) * 0.58;
                p = p.yzx * 2.03 + 17.1; value += Noise(p) * 0.28;
                return value + Noise(p.zxy * 2.01 + 11.7) * 0.14;
            }

            float3 PixelDirection(float3 direction, float2 screenPosition)
            {
                float size = max(1, floor(_ScaledScreenParams.y / max(96, _PixelResolution) + 0.5));
                float2 offset = (floor(screenPosition / size) + 0.5) * size - screenPosition;
                float depth = -mul((float3x3)UNITY_MATRIX_V, direction).z;
                float3 ray = direction / max(depth, 0.00001);
                ray += ddx(ray) * offset.x + ddy(ray) * offset.y;
                return normalize(ray);
            }

            float3 CelestialDirection(float yaw, float elevation)
            {
                yaw = radians(yaw); elevation = radians(elevation);
                return float3(sin(yaw) * cos(elevation), sin(elevation), cos(yaw) * cos(elevation));
            }

            float Galaxy(float3 d, float4 shape, float flattening)
            {
                float3 centre = CelestialDirection(shape.x, shape.y);
                float3 right = float3(cos(radians(shape.x)), 0, -sin(radians(shape.x)));
                float3 up = cross(centre, right);
                float front = dot(d, centre);
                if (front < 0.25) return 0;
                float2 q = float2(dot(d, right), dot(d, up)) / (front * tan(radians(clamp(shape.z, 1, 55))));
                float s, c; sincos(radians(shape.w), s, c);
                q = float2(c * q.x - s * q.y, s * q.x + c * q.y);
                q.y /= flattening;
                float radius = length(q);
                if (radius > 1.2) return 0;
                // Broad, static spiral arms survive low-resolution rendering without crawling.
                float arms = pow(saturate(0.5 + 0.5 * cos(atan2(q.y, q.x) * 2 - log(max(radius, 0.04)) * 5.5)), 3);
                float dust = lerp(0.6, 1, Noise(d * 45 + _Seed));
                float disc = exp(-radius * radius * 3) * (1 - smoothstep(0.8, 1.2, radius));
                return ((0.12 + arms * 0.8) * disc + exp(-radius * radius * 100) * 0.55) * dust;
            }

            float3 DayStars(float3 d, float galaxy)
            {
                float3 p = d * 85 + _Seed * float3(0.17, 0.31, 0.23);
                float3 cell = floor(p);
                float3 jitter = float3(Hash(cell + 1.7), Hash(cell + 9.2), Hash(cell + 21.3)) * 0.18 - 0.09;
                float core = 1 - smoothstep(0.1, 0.3, length(frac(p) - 0.5 - jitter));
                float visible = step(Hash(cell + 53), _StarDensity * 0.16 + galaxy * 0.12);
                float3 tint = lerp(float3(0.74,0.84,1), float3(1,0.9,0.68), Hash(cell + 8.1));
                float3 stars = tint * core * visible * lerp(0.35, 0.8, Hash(cell + 13));
                // A few larger four-point stars; their centres and brightness are stable.
                p = d * 26 + _Seed * float3(0.37, 0.13, 0.29);
                cell = floor(p);
                float3 a = abs(frac(p) - 0.5);
                float crossDistance = min(max(a.x,a.y), min(max(a.y,a.z), max(a.z,a.x)));
                float rays = (1 - smoothstep(0.025, 0.065, crossDistance)) * (1 - smoothstep(0.25, 0.4, max(a.x,max(a.y,a.z))));
                float brightCore = 1 - smoothstep(0.07, 0.18, length(a));
                stars += float3(0.9,0.94,1) * (brightCore + rays * 0.35) * step(Hash(cell + 53), _StarDensity * 0.22);
                return stars * _StarVisibility * step(0.0001, _StarDensity);
            }

            float3 Moon(float3 d, float3 sun, float3 sky, float3 atmosphere, float4 shape, float opacity, float3 tint)
            {
                float3 centre = CelestialDirection(shape.x, shape.y);
                float3 right = float3(cos(radians(shape.x)), 0, -sin(radians(shape.x)));
                float3 up = cross(centre, right);
                float front = dot(d, centre);
                float2 q = float2(dot(d, right), dot(d, up)) / max(0.001, front * tan(radians(clamp(shape.z, 0.5, 15))));
                float radius2 = dot(q, q);
                float aa = max(fwidth(radius2), 0.005);
                float disc = (1 - smoothstep(1 - aa, 1 + aa, radius2)) * step(0, front);
                float3 normal = q.x * right + q.y * up - sqrt(saturate(1 - radius2)) * centre;
                float phase = smoothstep(-0.08, 0.45, dot(normal, sun) + shape.w);
                float markings = lerp(0.76, 1, Fractal(float3(q * 6, _Seed * 0.1 + shape.x)));
                disc *= smoothstep(0, 0.12, d.y) * step(0.0001, opacity);
                // Even the shadowed lunar hemisphere occludes more distant stars and galaxies.
                sky = lerp(sky, atmosphere, disc);
                return lerp(sky, tint * markings, disc * (phase * 0.94 + 0.06) * opacity);
            }

            float3 LivingSun(float3 d, float3 sun, float3 sky, float power, float visibility)
            {
                float alignment = dot(d, sun);
                float radius = radians(clamp(_SunRadius, 0.2, 10));
                float halo = exp(-max(0, 1 - alignment) / (radius * radius * 5));
                sky += _SunTint.rgb * halo * _SunHalo * visibility;
                float3 right = normalize(cross(abs(sun.y) < 0.95 ? float3(0,1,0) : float3(1,0,0), sun));
                float3 up = cross(sun, right);
                float2 q = float2(dot(d,right), dot(d,up)) / max(0.001, alignment * tan(radius));
                float r = length(q);
                float aa = max(fwidth(r), 0.015);
                if (alignment <= 0 || r > 3.5) return sky;
                float t = _Time.y * _SunSpeed;
                float2 rim = q / max(r, 0.001);
                float angle = atan2(rim.y, rim.x);
                float flow = Noise(float3(rim * 3.5, t * 0.1)) * 3;
                float streams = pow(saturate(0.5 + 0.5 * sin(angle * 12 + flow - t * 0.18)), 4);
                float wisps = pow(saturate(0.5 + 0.5 * sin(angle * 29 + flow + t * 0.3)), 6);
                float edge = max(0, r - 1);
                float corona = exp(-edge * 4 / (0.24 + streams * 0.85 + wisps * 0.18));
                corona *= (0.3 + streams * 0.7) * (1 - smoothstep(2.7, 3.5, r));
                sky += lerp(_SunEmber.rgb, _SunTint.rgb, exp(-edge * 3)) * corona * _SunCorona * visibility;

                float limb = sqrt(saturate(1 - r * r));
                float3 surface = float3(q, limb);
                float s,c; sincos(t * 0.08,s,c);
                surface.xz = float2(c * surface.x + s * surface.z, -s * surface.x + c * surface.z);
                float plasma = Fractal(surface * 4.5 + float3(0,t * 0.12,t * 0.04));
                float grain = Noise(surface * 22 + t * 0.06);
                float heat = smoothstep(0.22,0.75,plasma * 0.8 + grain * 0.2);
                heat = floor(heat * 5 + 0.5) / 5;
                float3 surfaceColor = lerp(_SunEmber.rgb, _SunTint.rgb, lerp(1, heat, _SunDetail));
                surfaceColor *= lerp(0.72,1.15,pow(limb,0.45)) * min(power,1.4);
                float disc = 1 - smoothstep(1 - aa,1 + aa,r);
                return lerp(sky, surfaceColor, disc * visibility);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = PixelDirection(input.direction, input.positionCS.xy);
                float h = saturate(d.y);
                float3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, smoothstep(0, 0.32, h));
                sky = lerp(sky, _ZenithColor.rgb, pow(h, 0.7));
                sky = lerp(_GroundColor.rgb, sky, smoothstep(-0.22, 0.015, d.y));

                float sunLength = length(_MainLightPosition.xyz);
                float3 sun = _MainLightPosition.xyz / max(sunLength, 0.0001);
                float sunPower = max(_MainLightColor.r, max(_MainLightColor.g, _MainLightColor.b));
                float sunVisible = saturate(sunPower) * step(0.01, sunLength) * smoothstep(-0.04, 0.04, sun.y);
                float alignment = dot(d, sun);
                float3 atmosphere = sky;
                float veil = smoothstep(0.025,0.35,d.y) * (1 - pow(saturate(alignment),18) * 0.8 * sunVisible);
                float galaxy = Galaxy(d,_GalaxyOne,0.48) + Galaxy(d,_GalaxyTwo,0.65);
                sky = lerp(sky, _GalaxyColor.rgb, saturate(galaxy * _GalaxyVisibility * veil));
                sky += DayStars(d,galaxy) * veil;
                sky = Moon(d,sun,sky,atmosphere,float4(_MoonYaw,_MoonElevation,_MoonRadius,_MoonPhase),_MoonOpacity,_MoonColor.rgb);
                sky = Moon(d,sun,sky,atmosphere,_MoonTwo,_MoonOpacity * 0.7,lerp(_MoonColor.rgb,_GalaxyColor.rgb,0.3));
                sky = Moon(d,sun,sky,atmosphere,_MoonThree,_MoonOpacity * 0.55,_MoonColor.rgb);
                // No sun direction is needed when the scene has no active directional light.
                if (sunVisible > 0) sky = LivingSun(d,sun,sky,sunPower,sunVisible);

                // A stationary spherical 3D noise field: no UV seam or polar singularity.
                // Wind translates the field slowly; the camera position never enters it.
                float3 p = d * _CloudScale * float3(1, 1.7, 1) + _Seed * 0.13;
                p += _Wind.xyz * (_Time.y * _Wind.w);
                float broad = Noise(p * 0.65 + 7.3);
                float density = Fractal(p + (broad - 0.5) * 0.65);
                float threshold = lerp(0.85, 0.25, _CloudCoverage);
                float cloud = smoothstep(threshold, threshold + _CloudSoftness, density);
                cloud *= smoothstep(0.015, 0.18, d.y) * smoothstep(0, 0.08, _CloudCoverage);
                float shade = saturate((density - threshold) * 3.2 + h * 0.3);
                float bands = max(2, floor(_CloudSteps));
                shade = floor(shade * (bands - 1) + 0.5) / (bands - 1);
                float3 cloudColor = lerp(_CloudShadow.rgb, _CloudLight.rgb, shade);
                float rim = (1 - smoothstep(threshold, threshold + 0.13, density)) * pow(saturate(alignment), 10);
                cloudColor += _SunTint.rgb * rim * 0.22 * sunVisible;
                // All celestial layers sit behind clouds. Screen dithering is applied once by URP.
                sky = lerp(sky, cloudColor, cloud);
                return half4(max(sky, 0) * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
