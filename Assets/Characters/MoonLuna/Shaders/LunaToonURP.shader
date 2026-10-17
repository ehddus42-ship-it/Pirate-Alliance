// Unity 6 / URP 17. Main directional light, ambient SH, soft two-tone shading.
// This is a starter shader, not a reproduction of Honkai: Star Rail's renderer.
Shader "MoonLuna/Toon URP"
{
    Properties
    {
        [MainTexture] _MainTex("Base Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _ShadowColor("Shadow Tint", Color) = (0.65,0.68,0.83,1)
        _ShadowThreshold("Light / Shadow Threshold", Range(-1,1)) = 0.1
        _ShadowSoftness("Transition Softness", Range(0.001,0.5)) = 0.08
        _AmbientStrength("Ambient Strength", Range(0,2)) = 0.6
        [Toggle] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 1
        [Toggle] _UseEmission("Use Emission", Float) = 0
        _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,1)
        [Toggle] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
        // The setup tool transfers these states for transparent source materials.
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
        // Screen-door fade (1 = solid), set per renderer, e.g. LunaOcclusionFade on the floating moon.
        [HideInInspector] _Fade("Fade", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BumpMap_ST;
            float4 _EmissionMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            half4 _EmissionColor;
            float _ShadowThreshold;
            float _ShadowSoftness;
            float _AmbientStrength;
            float _UseNormalMap;
            float _BumpScale;
            float _UseEmission;
            float _AlphaClip;
            float _Cutoff;
            float _Cull;
            float _Surface;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Fade;
        CBUFFER_END

        struct LunaAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct LunaVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 tangentWS : TEXCOORD3;
            half fogFactor : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        LunaVaryings LunaVertex(LunaAttributes input)
        {
            LunaVaryings output = (LunaVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = p.positionCS;
            output.positionWS = p.positionWS;
            output.normalWS = n.normalWS;
            output.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = input.uv;
            output.fogFactor = ComputeFogFactor(p.positionCS.z);
            return output;
        }

        half4 LunaBase(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)) * _BaseColor;
        }

        void LunaClip(half alpha)
        {
            if (_AlphaClip > 0.5) clip(alpha - _Cutoff);
        }

        // Ordered 4x4 dither: drops a growing share of pixels as _Fade goes to 0 (no sorting, still writes depth).
        void LunaFadeClip(float4 positionCS)
        {
            if (_Fade >= 0.999) return;
            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = uint2(positionCS.xy) & 3u;
            clip(_Fade - (bayer[p.y * 4u + p.x] + 0.5) / 16.0);
        }

        half3 LunaNormal(LunaVaryings input, bool isFrontFace)
        {
            half3 n = normalize(input.normalWS);
            if (_UseNormalMap > 0.5 && dot(input.tangentWS.xyz, input.tangentWS.xyz) > 0.001)
            {
                half3 t = normalize(input.tangentWS.xyz);
                half3 b = cross(n, t) * input.tangentWS.w;
                half3 mapNormal = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(input.uv, _BumpMap)),
                    _BumpScale);
                n = normalize(mul(mapNormal, half3x3(t, b, n)));
            }
            // Double-sided cloth uses the normal facing the camera.
            return isFrontFace ? n : -n;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LunaVertex
            #pragma fragment LunaFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            half4 LunaFragment(LunaVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LunaFadeClip(input.positionCS);
                half4 baseSample = LunaBase(input.uv);
                LunaClip(baseSample.a);
                half3 normalWS = LunaNormal(input, IS_FRONT_VFACE(facing, true, false));
                // Recompute cascade coordinates per pixel; vertex interpolation crosses cascade boundaries.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    mainLight.shadowAttenuation = lerp(mainLight.shadowAttenuation, 1.0h,
                        GetMainLightShadowFade(input.positionWS));
                #endif
                half transition = smoothstep(_ShadowThreshold - max(_ShadowSoftness, 0.001),
                    _ShadowThreshold + max(_ShadowSoftness, 0.001), dot(normalWS, mainLight.direction));
                transition *= mainLight.shadowAttenuation;
                half3 toonTint = lerp(_ShadowColor.rgb, half3(1,1,1), transition);
                half3 ambient = max(SampleSH(normalWS), half3(0,0,0)) * _AmbientStrength;
                half3 lit = baseSample.rgb * toonTint * (ambient + mainLight.color * mainLight.distanceAttenuation);
                if (_UseEmission > 0.5)
                    lit += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap,
                        TRANSFORM_TEX(input.uv, _EmissionMap)).rgb * _EmissionColor.rgb;
                return half4(MixFog(lit, input.fogFactor), baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LunaShadowVertex
            #pragma fragment LunaShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            LunaVaryings LunaShadowVertex(LunaAttributes input)
            {
                LunaVaryings output = LunaVertex(input);
                float3 lightDirection = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    lightDirection = normalize(_LightPosition - output.positionWS);
                #endif
                float3 biased = ApplyShadowBias(output.positionWS, output.normalWS, lightDirection);
                output.positionCS = TransformWorldToHClip(biased);
                // Directional shadows need near-plane clamping; punctual lights must keep their clip depth.
                #if !defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    #if UNITY_REVERSED_Z
                        output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                    #else
                        output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                    #endif
                #endif
                return output;
            }

            half4 LunaShadowFragment(LunaVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                LunaClip(LunaBase(input.uv).a);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LunaVertex
            #pragma fragment LunaDepthFragment
            #pragma multi_compile_instancing
            half4 LunaDepthFragment(LunaVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LunaFadeClip(input.positionCS);
                LunaClip(LunaBase(input.uv).a);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LunaVertex
            #pragma fragment LunaNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 LunaNormalsFragment(LunaVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LunaFadeClip(input.positionCS);
                LunaClip(LunaBase(input.uv).a);
                half3 normalWS = LunaNormal(input, IS_FRONT_VFACE(facing, true, false));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 encoded = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                    return half4(PackFloat2To888(encoded), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
