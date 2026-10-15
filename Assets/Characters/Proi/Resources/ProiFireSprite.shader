// Punch fire cut out of the reference illustration (ProiPunchFire.png, premultiplied, on an axial billboard round
// the punch direction). _Dissolve burns it away with a ragged edge.
// Live fire (_Distort = master strength, 0 = still art): the flames stream from the fist to the tail and wave more the
// further they get from the fist, the dim outer tongues lick in and out (_Lick), hot cel patches run down the flame and
// the whole fire flickers (_Flicker).
// _Reveal 0..1 grows the fire from the fist (u = _PivotU) back to the tail (u = 1) with a ragged flame front.
// Two passes so the body never disappears behind the fire and the fire never disappears behind the body:
//  - in front of the character (depth test passes): drawn in full
//  - behind / inside the character (depth test fails): drawn faintly (_OccludedAlpha), showing through
Shader "Proi/FireSprite"
{
    Properties
    {
        _MainTex ("Fire (premultiplied)", 2D) = "black" {}
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Dissolve ("Dissolve", Range(0, 1.2)) = 0
        _Distort ("Flow (0 = still art)", Range(0, 2)) = 1
        _FlowSpeed ("Flow Speed", Range(0, 3)) = 1
        _Lick ("Licking Tongues", Range(0, 1.5)) = 0.6
        _Flicker ("Flicker / Hot Patches", Range(0, 2)) = 1
        _Reveal ("Reveal (fist -> tail)", Range(0, 1)) = 1
        _PivotU ("Fist U", Range(0, 1)) = 0.09
        _TailAlpha ("Tail Alpha", Range(0, 1)) = 0.3
        _OccludedAlpha ("Alpha Behind The Body", Range(0, 1)) = 0.25
        _Intensity ("Intensity (HDR, blooms)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Alpha;
            float _Dissolve;
            float _Distort;
            float _FlowSpeed;
            float _Lick;
            float _Flicker;
            float _Intensity;
            float _Reveal;
            float _PivotU;
            float _TailAlpha;
            float _OccludedAlpha;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
        }

        Varyings vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.uv = input.uv;
            return o;
        }

        half4 Fire(Varyings i, half scale)
        {
            float t = _Time.y;
            float ft = t * _FlowSpeed;
            float along = saturate((i.uv.x - _PivotU) / max(1.0 - _PivotU, 1e-3));   // 0 at the fist .. 1 tail
            // the fire streams from the fist to the tail (patterns travel +u), waving harder toward the tail
            float amp = lerp(0.15, 1.0, along) * _Distort;
            float n1 = Noise(float2((i.uv.x - ft * 0.9) * 5.0, i.uv.y * 4.0 + ft * 0.3));
            float n2 = Noise(float2((i.uv.x - ft * 1.6) * 12.0, i.uv.y * 10.0 - ft * 0.8));
            float wave = sin(i.uv.x * 15.0 - ft * 16.0 + i.uv.y * 3.0);
            float2 uv = i.uv;
            uv.y += ((n1 - 0.5) * 0.09 + (n2 - 0.5) * 0.03 + wave * 0.02) * amp;
            uv.x += ((n2 - 0.5) * 0.03 - abs(wave) * 0.012) * amp;
            half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uv, 0.001, 0.999));

            float n = Noise(i.uv * 9.0) * 0.65 + Noise(i.uv * 23.0) * 0.35;
            half vis = saturate((n - _Dissolve) * 7.0 + 0.5) * _Alpha;   // burns away in ragged patches
            float jag = Noise(float2(i.uv.y * 9.0, t * 5.0)) * 0.6 + Noise(i.uv * float2(20.0, 30.0)) * 0.4;
            float front = _Reveal * 1.3 - along + (jag - 0.5) * 0.3;               // ragged tongues lead the front
            vis *= saturate(front / 0.09);
            vis *= lerp(1.0, _TailAlpha, smoothstep(0.3, 0.95, along));          // tail fades: the body stays readable

            // licking tongues: the dim (dark red) cel bands flick in and out with the flowing noise, the white core holds
            half3 col = c.rgb / max(c.a, 1e-3);
            half heat = saturate(dot(col, half3(0.25, 0.55, 0.2)));
            float e = Noise(float2((i.uv.x - ft * 1.2) * 9.0, i.uv.y * 12.0 + ft * 0.6)) * 0.7 + n2 * 0.3;
            float cut = (e - 0.38) * _Lick * (0.45 + along) * saturate(_Distort);
            vis *= saturate((heat - cut) * 25.0);
            vis *= scale;

            // hot cel patches running down the flame + an overall flicker
            float hot = step(0.72, Noise(float2((i.uv.x - ft * 1.3) * 6.0, i.uv.y * 3.0 + 7.0))) * step(0.4, heat);
            half k = _Flicker * saturate(_Distort);
            c.rgb += c.a * hot * k * half3(0.35, 0.3, 0.15);
            half flick = 1.0 + (Noise(float2(t * 18.0, 3.1)) - 0.5) * 0.3 * k;

            c.rgb += c.a * saturate(1.0 - abs(front - 0.04) / 0.05) * step(_Reveal, 0.999) * half3(0.5, 0.3, 0.1);   // hot flame front
            return half4(c.rgb * _Intensity * flick * vis, c.a * vis);
        }
        ENDHLSL

        Pass
        {   // in front of the character: full
            Name "FireSprite"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { return Fire(i, 1.0); }
            ENDHLSL
        }

        Pass
        {   // behind / inside the character: faint, so the body stays readable but the fire never vanishes
            Name "FireSpriteOccluded"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { return Fire(i, _OccludedAlpha); }
            ENDHLSL
        }
    }
}
