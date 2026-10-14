// Unity 6 / URP 17. Additive moonlight for LunaMoonFx: soft glow disc, star sparkles (particles) and the sprint
// light trail. _Mode 0 = soft round glow, 1 = four-point star, 2 = ribbon (soft across the trail's width).
// Vertex colour (particles, trail gradient) multiplies _Color.
Shader "NamgungLuna/Moon Glow"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.55, 0.8, 1.6, 1)
        [Enum(Glow,0,Star,1,Ribbon,2)] _Mode("Mode", Float) = 0
        _Falloff("Falloff", Range(0.5, 12)) = 3.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "MoonGlow"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Mode;
                float _Falloff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float a;
                if (_Mode < 0.5)
                {
                    a = exp(-dot(p, p) * _Falloff) * (1.0 - smoothstep(0.85, 1.0, length(p)));
                }
                else if (_Mode < 1.5)
                {
                    float r = length(p);
                    float core = exp(-r * r * 16.0);
                    float spikes = exp(-abs(p.x) * 26.0) * exp(-abs(p.y) * 3.0) + exp(-abs(p.y) * 26.0) * exp(-abs(p.x) * 3.0);
                    a = saturate(core * 1.3 + spikes * 0.9) * saturate(1.0 - r);
                }
                else
                {
                    float v = input.uv.y * 2.0 - 1.0;
                    a = exp(-v * v * _Falloff) * (0.6 + 0.4 * exp(-input.uv.x * 6.0));
                }
                half4 c = _Color * input.color;
                a *= c.a;
                return half4(c.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
