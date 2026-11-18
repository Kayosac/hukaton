Shader "Hakaton/Cards/Sonar silhouette"
{
    // «Эхолот»: a flat glowing copy of an enemy (its sprite's alpha, or a mesh), and the wave's ring. Drawn twice:
    // bright where something stands in front (ZTest Greater: the enemy behind a wall or rock), faint where it stands in
    // the open (ZTest LEqual) — the same two-pass trick as Merchant's nose's beacon. Colour: the tint, times the vertex
    // colour when _VertexColour is 1 (a SpriteRenderer's or LineRenderer's colour carries the fade).
    Properties
    {
        [PerRendererData] _MainTex ("Texture (alpha only)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _VertexColour ("Use vertex colour", Range(0, 1)) = 1
        _Glow ("Glow", Range(0, 4)) = 1.4
        _Hidden ("Behind walls", Range(0, 2)) = 1
        _Open ("In the open", Range(0, 1)) = .3
        _Cutoff ("Alpha edge", Range(0, 1)) = .35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        Offset -1, -1
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Tint;
            half _VertexColour;
            half _Glow;
            half _Hidden;
            half _Open;
            half _Cutoff;
        CBUFFER_END
        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.color = lerp(half4(1, 1, 1, 1), input.color, _VertexColour);
            return output;
        }
        half4 Shade(Varyings input, half strength)
        {
            half texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
            // A crisp silhouette, as pixel art: the drawing's soft edge becomes a short ramp.
            half shape = smoothstep(_Cutoff * .6, _Cutoff, texel);
            half4 colour = input.color * _Tint;
            return half4(colour.rgb * _Glow, saturate(colour.a * shape * strength));
        }
        ENDHLSL

        Pass
        {
            Name "BehindWalls"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZTest Greater
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target { return Shade(input, _Hidden); }
            ENDHLSL
        }
        Pass
        {
            Name "InTheOpen"
            Tags { "LightMode"="UniversalForward" }
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target { return Shade(input, _Open); }
            ENDHLSL
        }
    }
}
