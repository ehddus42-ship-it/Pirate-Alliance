Shader "NamgungLuna/Combat/Moon Surface"
{
    Properties
    {
        _MainTex ("Cratered moon", 2D) = "white" {}
        [HDR] _Tint ("Moonlight", Color) = (1,0.94,0.82,1)
        [HDR] _Rim ("Gold limb", Color) = (1.5,0.83,0.2,1)
        _Intensity ("Surface radiance", Range(0,5)) = 1.2
        _Fade ("Character occlusion fade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Tint, _Rim;
                float _Intensity, _Fade;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD1; float3 view:TEXCOORD2; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o; float3 ws=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(ws); o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.view=GetWorldSpaceViewDir(ws); o.uv=v.uv; o.color=v.color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                half4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                clip(t.a-0.08);
                float dither=frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))));
                clip(_Fade*i.color.a*_Tint.a-dither-.001);
                float fresnel=pow(1-saturate(abs(dot(normalize(i.normalWS),normalize(i.view)))),2);
                half3 surface=pow(max(t.rgb,0.001),1.12)*_Tint.rgb*_Intensity;
                surface=lerp(surface*half3(0.35,0.29,0.58),surface,i.color.r);
                surface+=_Rim.rgb*(fresnel*0.45+i.color.g*0.22);
                return half4(surface,1);
            }
            ENDHLSL
        }
    }
}
