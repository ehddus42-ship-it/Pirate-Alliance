// Geumbi toon look for URP (port of the Blender GeumbiToon group):
// two-tone cel (main light N.L + shadows -> base * Shadow Tint), hard spec + lit-side rim screened on top,
// inverted-hull outline pass (object-space push along the skinned normal, front faces culled).
Shader "Geumbi/Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _ShadowTint ("Shadow Tint", Color) = (0.7,0.65,0.8,1)
        _Threshold ("Shadow Threshold (N.L)", Range(-1,1)) = 0.05
        _Softness ("Shadow Softness", Range(0.001,0.5)) = 0.04
        _SpecStrength ("Spec Strength", Range(0,1)) = 0
        _SpecSize ("Spec Size", Range(0.8,1)) = 0.97
        _RimStrength ("Rim Strength", Range(0,1)) = 0.2
        _RimWidth ("Rim Width", Range(0,1)) = 0.32
        _Ambient ("Ambient (SH) Amount", Range(0,1)) = 0.15
        _OutlineColor ("Outline Color", Color) = (0.035,0.028,0.045,1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.0012
        // used by the borrowed URP Lit shadow/depth passes
        [HideInInspector] _Cutoff ("Alpha Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half4 _ShadowTint;
            half _Threshold, _Softness, _SpecStrength, _SpecSize, _RimStrength, _RimWidth, _Ambient;
            half4 _OutlineColor;
            float _OutlineWidth;
            half _Cutoff;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; float fog : TEXCOORD4;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half3 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 bitan = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                float3 N = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitan, i.normalWS)));
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half lit = smoothstep(_Threshold, _Threshold + _Softness, dot(N, L.direction));
                lit *= smoothstep(0.3, 0.6, L.shadowAttenuation);
                half3 col = lerp(base * _ShadowTint.rgb, base, lit) * L.color;
                half spec = smoothstep(_SpecSize, _SpecSize + 0.01, dot(N, normalize(L.direction + V))) * _SpecStrength;
                half rim = smoothstep(1 - _RimWidth, 1 - _RimWidth + 0.04, 1 - saturate(dot(N, V))) * _RimStrength;
                half hl = saturate((spec + rim) * lit);
                col = 1 - (1 - col) * (1 - hl);                       // screen, like the Blender group
                col += base * SampleSH(N) * _Ambient;
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float fog : TEXCOORD0; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 p = i.positionOS.xyz + normalize(i.normalOS) * _OutlineWidth;
                o.positionCS = TransformObjectToHClip(p); o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag (Varyings i) : SV_Target { return half4(MixFog(_OutlineColor.rgb, i.fog), 1); }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
