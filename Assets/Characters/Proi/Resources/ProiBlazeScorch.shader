// Burnt patch left on the ground by a flame blast (Blender build_scorch): a dark disc with a noisy rim on the
// shockwave disc mesh (radius 1, normal = mesh +Z), glowing ember-red in the middle while it is fresh.
Shader "Proi/BlazeScorch"
{
    Properties
    {
        _Fade ("Fade", Float) = 0
        _Glow ("Glow", Float) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BlazeScorch"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProiBlazeNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Fade, _Glow, _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posOS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.posOS = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float n = BlazeNoise4(i.posOS * 4.0, _Seed, 4, 0.55, 0.3);
                float d = length(i.posOS.xy) + (n - 0.5) * 0.5;
                float alpha = (1.0 - smoothstep(0.55, 1.0, d)) * 0.85 * _Fade;
                float glow = (1.0 - smoothstep(0.35, 0.75, d)) * _Glow;
                float3 ember = lerp(float3(0.25, 0.0, 0.0), float3(1.0, 0.3, 0.02), saturate(glow)) * glow * 3.0;
                return half4(float3(0.01, 0.008, 0.008) + ember, saturate(alpha + glow * 0.5));
            }
            ENDHLSL
        }
    }
}
