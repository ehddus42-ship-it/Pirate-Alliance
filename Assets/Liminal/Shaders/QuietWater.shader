Shader "Liminal/Quiet Water"
{
    Properties
    {
        _DeepColor("Deep pool", Color) = (0.055,0.25,0.27,1)
        _ShallowColor("Reflected light", Color) = (0.31,0.65,0.61,1)
        _TileColor("Submerged grout", Color) = (0.11,0.36,0.36,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        Pass
        {
            Name "QuietWater"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _TileColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.world.xz;
                float t = _Time.y * .12;
                float2 wobble = float2(sin(p.y * 2.3 + t), cos(p.x * 2.7 - t)) * .024;
                float2 grid = abs(frac((p + wobble) * 1.4) - .5);
                float grout = smoothstep(.47, .49, max(grid.x,grid.y));
                float waves = sin(p.x*2.1+t+sin(p.y*1.2-t)) * sin(p.y*2.4-t);
                float caustic = pow(saturate(.6 + .4 * waves), 8) * .21;
                float reflection = pow(saturate(cos((p.x+p.y*.12+wobble.x)*1.05)), 34) * .24;
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, .30 + caustic + reflection);
                color = lerp(color,_TileColor.rgb,grout*.38);
                color += half3(.24,.34,.28) * caustic;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
