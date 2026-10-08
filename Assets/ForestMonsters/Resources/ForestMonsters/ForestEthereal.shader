Shader "Forest/Ethereal Wind"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (.38,.95,.72,.6)
        _Flow ("Flow", Float) = 1
        _Softness ("Soft edge", Range(.01,.49)) = .18
        _Roundness ("Round particle", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor; float _Flow; float _Softness; float _Roundness;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.color=v.color; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float edge=smoothstep(0,_Softness,i.uv.y)*smoothstep(0,_Softness,1-i.uv.y);
                edge*=lerp(1,1-smoothstep(.06,.5,length(i.uv-.5)),_Roundness);
                float flow=.72+.28*sin(i.uv.x*39-_Time.y*_Flow*7+sin(i.uv.x*17+_Time.y*2));
                half4 c=_BaseColor*i.color; c.a*=edge*flow; return c;
            }
            ENDHLSL
        }
    }
}
