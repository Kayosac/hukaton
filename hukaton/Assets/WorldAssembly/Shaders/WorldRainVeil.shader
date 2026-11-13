// Rain hanging from a cloud of the endless world's rain fronts (WorldRain): a grey veil of falling streaks between the
// cloud and the ground, turned to the camera. Like the clouds it is seen far through the fog (_FogScale of the scene's
// exp² fog); near the camera it fades out, where the camera's own rain falls. _Amount: how hard it rains (0…1).
Shader "Hakaton/World Rain Veil"
{
    Properties
    {
        _BaseColor("Colour (alpha: at full rain)", Color) = (0.3, 0.33, 0.38, 0.55)
        _Amount("Rain", Range(0, 1)) = 1
        _Streaks("Streaks across", Float) = 28
        _Speed("Fall speed (heights a second)", Float) = 1.4
        _FogDensity("Scene fog density (exp²)", Float) = 0.024
        _FogScale("Share of the scene's fog on the rain", Range(0, 1)) = 0.35
        _Near("Faded out nearer than (m)", Float) = 16
        _NearFade("Fades in over (m)", Float) = 24
        _Far("Gone at (m)", Float) = 210
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Veil"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/WorldAssembly/Shaders/WorldGrade.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Amount;
                float _Streaks;
                float _Speed;
                float _FogDensity;
                float _FogScale;
                float _Near;
                float _NearFade;
                float _Far;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.position.xyz);
                output.position = TransformWorldToHClip(output.world);
                output.uv = input.uv;
                return output;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            half4 frag(Varyings input) : SV_Target
            {
                float d = distance(input.world, _WorldSpaceCameraPos);
                float k = _FogDensity * _FogScale * d;
                float seen = exp(-k * k) * (1 - smoothstep(_Far * 0.7, _Far, d)) * smoothstep(_Near, _Near + max(_NearFade, 1), d);
                float2 uv = input.uv;
                // Streaks: columns of soft dashes falling at slightly different speeds.
                float column = floor(uv.x * _Streaks);
                float h = Hash(column + 1);
                float v = frac(uv.y * (3 + h * 3) + _Time.y * _Speed * (0.8 + 0.5 * h) + h * 7);
                float streak = smoothstep(0.0, 0.35, v) * (1 - smoothstep(0.55, 1.0, v));
                float across = frac(uv.x * _Streaks);
                streak *= smoothstep(0.0, 0.3, across) * (1 - smoothstep(0.55, 0.9, across));
                // Thin at the sides, thinning out under the cloud and at the ground.
                float shape = smoothstep(0.0, 0.25, uv.x) * (1 - smoothstep(0.75, 1.0, uv.x)) * smoothstep(0.0, 0.08, uv.y) * (1 - smoothstep(0.85, 1.0, uv.y));
                float alpha = _BaseColor.a * _Amount * shape * (0.6 + 0.4 * streak) * seen;
                return half4(WorldGrade(_BaseColor.rgb), alpha);
            }
            ENDHLSL
        }
    }
}
