Shader "Liminal/Architectural Surface"
{
    Properties
    {
        _BaseMap("Surface",2D)="white"{}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _BumpMap("Fine normal",2D)="bump"{}
        _BumpScale("Normal strength",Float)=0.24
        _WorldScale("Repeats per metre",Float)=0.5
        _Smoothness("Smoothness",Range(0,1))=0.2
        _Metallic("Metallic",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _BumpScale, _WorldScale, _Smoothness, _Metallic;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ArchitecturalSurface"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float fog:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half3 Shade(Light light,half3 normal,half3 view,half3 albedo)
            {
                half ndl=saturate(dot(normal,light.direction));half3 h=normalize(light.direction+view);
                half spec=pow(saturate(dot(normal,h)),lerp(10,90,_Smoothness))*_Smoothness*.16;
                return (albedo*ndl+spec)*light.color*light.distanceAttenuation*light.shadowAttenuation;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 n=normalize(i.normal);half3 w=pow(abs(n),8);w/=max(.0001,w.x+w.y+w.z);
                float3 p=i.world*_WorldScale;
                half3 tx=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.zy).rgb;
                half3 ty=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xz).rgb;
                half3 tz=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xy).rgb;
                half3 albedo=(tx*w.x+ty*w.y+tz*w.z)*_BaseColor.rgb;
                half3 nx=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.zy),_BumpScale);
                half3 ny=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.xz),_BumpScale);
                half3 nz=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.xy),_BumpScale);
                half3 mapped=half3(nx.z*sign(n.x),nx.y,nx.x)*w.x+half3(ny.x,ny.z*sign(n.y),ny.y)*w.y+half3(nz.x,nz.y,nz.z*sign(n.z))*w.z;
                n=normalize(mapped);
                half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                InputData inputData=(InputData)0;
                inputData.positionWS=i.world;
                inputData.normalWS=n;
                inputData.viewDirectionWS=view;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                half3 color=albedo*max(SampleSH(n),half3(.12,.14,.13))*ao.indirectAmbientOcclusion;
                color+=Shade(GetMainLight(TransformWorldToShadowCoord(i.world)),n,view,albedo)*ao.directAmbientOcclusion;
                #ifdef _ADDITIONAL_LIGHTS
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    color+=Shade(GetAdditionalLight(lightIndex,i.world),n,view,albedo)*ao.directAmbientOcclusion;
                }
                #endif
                uint count=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    color+=Shade(GetAdditionalLight(lightIndex,i.world),n,view,albedo)*ao.directAmbientOcclusion;
                LIGHT_LOOP_END
                #endif
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        // Contact occlusion follows the architectural geometry rather than the
        // fine normal-map grain used only by the forward lighting pass.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
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
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
