// Unity 6 / URP 17. Moonlight ripple laid flat on the floor where Moon Luna's toe touches (LunaMoonFx).
// A quad in UV space: a bright leading ring, a fainter inner ring and a soft fill expand and fade with _Progress.
Shader "MoonLuna/Moon Ripple"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.55, 0.8, 1.6, 1)
        _Progress("Progress", Range(0, 1)) = 0.3
        _Strength("Strength", Range(0, 2)) = 1
        _Width("Ring Width", Range(0.005, 0.2)) = 0.045
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "MoonRipple"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Progress;
                float _Strength;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float t = saturate(_Progress);
                float radius = 1.0 - pow(1.0 - t, 2.4);            // fast out, slow settle
                float width = _Width * (0.7 + 1.3 * t);
                float lead = exp(-pow((r - radius * 0.94) / width, 2.0));
                float inner = exp(-pow((r - radius * 0.62) / (width * 0.75), 2.0)) * 0.5;
                float fill = saturate(1.0 - r / max(radius, 1e-3)) * 0.22 * (1.0 - t);
                float angle = atan2(p.y, p.x);
                float shimmer = 0.78 + 0.22 * sin(angle * 13.0 + t * 7.0) * sin(angle * 5.0 - t * 3.0);
                float fade = pow(1.0 - t, 1.5) * smoothstep(0.0, 0.05, t);
                float a = (lead * shimmer + inner + fill) * fade * _Strength * (1.0 - smoothstep(0.96, 1.0, r));
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
