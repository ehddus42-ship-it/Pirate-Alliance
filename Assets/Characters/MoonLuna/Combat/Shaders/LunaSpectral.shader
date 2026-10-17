Shader "MoonLuna/Combat/Spectral"
{
    Properties
    {
        _MainTex ("Organic texture", 2D) = "white" {}
        [HDR] _Tint ("Tint", Color) = (1,1,1,1)
        _Intensity ("Radiance", Range(0,8)) = 1.5
        _Flow ("Vapor movement", Range(0,1)) = 0.15
        _Colorize ("Tint from luminance", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Tint;
                float _Intensity, _Flow, _Colorize;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.color=v.color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.uv;
                p.x+=sin(p.y*16+_Time.y*1.7)*0.024*_Flow*sin(p.y*3.14159);
                p.y+=sin(p.x*11-_Time.y*1.1)*0.018*_Flow*sin(p.x*3.14159);
                half4 t=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,p);
                t.rgb=lerp(t.rgb,dot(t.rgb,half3(.2126,.7152,.0722)).xxx,_Colorize);
                half4 c=t*_Tint*i.color;
                c.rgb*=_Intensity;
                return c;
            }
            ENDHLSL
        }
    }
}
