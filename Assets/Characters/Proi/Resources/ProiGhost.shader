// Dash afterimage (ProiDash): a baked copy of Proi's body drawn as a cel-shaded fire silhouette - hot yellow rim, deep red
// core, licked away from the bottom up by flowing noise as it fades. Premultiplied; _Alpha (per ghost, property block) fades it.
Shader "Proi/Ghost"
{
    Properties
    {
        _CoreColor ("Core", Color) = (0.85, 0.12, 0.04, 1)
        _RimColor ("Rim", Color) = (1, 0.72, 0.25, 1)
        _Intensity ("Intensity (HDR, blooms)", Float) = 1.8
        _CoreAlpha ("Core alpha", Range(0, 1)) = 0.22
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Burn ("Burn away (0..1)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor, _RimColor;
                float _Intensity, _CoreAlpha, _Alpha, _Burn;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float3 posWS : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(o.posWS);
                return o;
            }

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS), v = normalize(i.viewWS);
                float fres = 1.0 - saturate(dot(n, v));
                // cel bands: core / mid / rim
                float band = fres > 0.62 ? 1.0 : (fres > 0.38 ? 0.55 : 0.0);
                half3 col = lerp(_CoreColor.rgb, _RimColor.rgb, band);
                float a = lerp(_CoreAlpha, 1.0, band);

                // flame licks eat it away as it fades (rising flow noise, cut hard for the cel look)
                float flow = noise(i.posWS * 7.0 - float3(0, _Time.y * 3.0, 0)) * 0.6 + noise(i.posWS * 15.0 - float3(0, _Time.y * 5.0, 0)) * 0.4;
                float cut = flow - _Burn * 1.15 + 0.1;
                clip(cut);
                col = cut < 0.12 ? _RimColor.rgb * 1.3 : col;   // a hot edge where it is burning away

                a *= _Alpha;
                return half4(col * _Intensity * a, a);
            }
            ENDHLSL
        }
    }
}
