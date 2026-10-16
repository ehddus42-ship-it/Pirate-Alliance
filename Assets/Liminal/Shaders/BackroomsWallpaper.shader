Shader "Liminal/Backrooms Wallpaper"
{
    Properties
    {
        _BaseMap("Paper surface", 2D) = "white" {}
        _BaseColor("Paper tint", Color) = (0.88, 0.81, 0.55, 1)
        _PatternColor("Ochre print", Color) = (0.56, 0.45, 0.23, 1)
        _WorldScale("Surface repeats per metre", Float) = 0.75
        _PatternScale("Ornaments per metre", Float) = 1.5
        _PatternStrength("Print strength", Range(0, 1)) = 0.38
        _Smoothness("Smoothness", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // All passes share this layout, including URP's depth and shadow passes.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _PatternColor;
            float _WorldScale;
            float _PatternScale;
            half _PatternStrength;
            half _Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "BackroomsWallpaper"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WallpaperVertex
            #pragma fragment WallpaperFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct WallpaperAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WallpaperVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            WallpaperVaryings WallpaperVertex(WallpaperAttributes input)
            {
                WallpaperVaryings output = (WallpaperVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float AntialiasedPrintLine(float distanceToLine, float halfWidth)
            {
                float pixelWidth = max(fwidth(distanceToLine), 0.0001);
                return saturate((halfWidth - abs(distanceToLine)) / pixelWidth + 0.5);
            }

            float LozengePrint(float2 wallPosition)
            {
                // World Y is vertical on both wall projections. A slightly tighter
                // horizontal repeat creates the tall, quiet rhythm of old wallpaper.
                float2 uv = wallPosition * max(_PatternScale, 0.001) * float2(1.15, 1.0);
                float2 footprint = fwidth(uv);
                float detailFade = 1.0 - smoothstep(0.035, 0.10, max(footprint.x, footprint.y));
                float2 q = frac(uv + 0.5) - 0.5;

                // Narrow, softly bowed diamonds with an inset diamond and a single
                // central dot. The motif stays well clear of the repeating cell edge.
                float2 outer = abs(q) / float2(0.265, 0.405);
                float outerDistance = (pow(outer.x, 1.14) + pow(outer.y, 1.14) - 1.0) * 0.21;
                float outline = AntialiasedPrintLine(outerDistance, 0.010);

                float2 inner = abs(q) / float2(0.105, 0.19);
                float innerDistance = (inner.x + inner.y - 1.0) * 0.093;
                float inset = AntialiasedPrintLine(innerDistance, 0.0065) * 0.65;

                float dotDistance = length(q);
                float dotAA = max(fwidth(dotDistance), 0.0001);
                float dotPrint = 1.0 - smoothstep(0.026 - dotAA, 0.026 + dotAA, dotDistance);

                return max(outline, max(inset, dotPrint * 0.8)) * detailFade;
            }

            half3 WallpaperLight(Light light, half3 normalWS, half3 viewWS, half3 albedo)
            {
                half diffuse = saturate(dot(normalWS, light.direction));
                half3 halfDirection = SafeNormalize(light.direction + viewWS);
                half specular = pow(saturate(dot(normalWS, halfDirection)), lerp(10.0, 90.0, _Smoothness));
                specular *= _Smoothness * 0.16 * diffuse;
                return (albedo * diffuse + specular) * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }

            half4 WallpaperFragment(WallpaperVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = normalize(input.normalWS);
                half3 weights = pow(abs(normalWS), 8.0);
                weights /= max(weights.x + weights.y + weights.z, 0.0001);

                float3 p = input.positionWS * max(_WorldScale, 0.001);
                half3 surfaceX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb;
                half3 surfaceY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xz * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb;
                half3 surfaceZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb;
                half3 paper = surfaceX * weights.x + surfaceY * weights.y + surfaceZ * weights.z;

                float patternX = LozengePrint(input.positionWS.zy);
                float patternZ = LozengePrint(input.positionWS.xy);
                float verticalMask = 1.0 - smoothstep(0.30, 0.75, abs(normalWS.y));
                half pattern = saturate((patternX * weights.x + patternZ * weights.z) * verticalMask * _PatternStrength);
                half3 albedo = paper * lerp(_BaseColor.rgb, _PatternColor.rgb, pattern);

                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif

                half4 shadowMask = half4(1, 1, 1, 1);
                half3 color = albedo * max(SampleSH(normalWS), half3(0.12, 0.14, 0.13)) * ao.indirectAmbientOcclusion;
                color += WallpaperLight(GetMainLight(shadowCoord, input.positionWS, shadowMask), normalWS, viewWS, albedo) * ao.directAmbientOcclusion;

                #ifdef _ADDITIONAL_LIGHTS
                    #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                            color += WallpaperLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, viewWS, albedo) * ao.directAmbientOcclusion;
                        }
                    #endif

                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        color += WallpaperLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, viewWS, albedo) * ao.directAmbientOcclusion;
                    LIGHT_LOOP_END
                #endif

                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }

        // SSAO uses Source=DepthNormals in the PC renderer. Supply geometric
        // normals so wallpaper walls participate in the depth/normal prepass.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
