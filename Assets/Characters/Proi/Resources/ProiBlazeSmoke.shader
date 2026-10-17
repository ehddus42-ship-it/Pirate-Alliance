// Dark smoke puffs left where a flame-combo hit landed (Blender MAT_Smoke): noisy soft sphere, alpha blended.
Shader "Proi/BlazeSmoke"
{
    Properties
    {
        _Density ("Density", Float) = 0
        _Seed ("Seed", Float) = 71
        _Color ("Colour", Color) = (0.06, 0.05, 0.05, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+45" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BlazeSmoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProiBlazeNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Density, _Seed;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posOS : TEXCOORD0; float3 nWS : TEXCOORD1; float3 vWS : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 pWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(pWS);
                o.posOS = input.positionOS.xyz;
                o.nWS = TransformObjectToWorldNormal(input.normalOS);
                o.vWS = GetWorldSpaceViewDir(pWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = _Time.y * 30.0;
                float facing = 1.0 - abs(dot(normalize(i.nWS), normalize(i.vWS)));
                float3 q = i.posOS * 2.5 + float3(0, 0, -0.08) * f;
                float val = BlazeNoise4(q, _Seed + 0.04 * f, 5, 0.55, 0.6) + _Density - facing * 0.9;
                return half4(_Color.rgb, smoothstep(0.4, 0.75, val) * 0.55);
            }
            ENDHLSL
        }
    }
}
