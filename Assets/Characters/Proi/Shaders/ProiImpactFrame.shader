// 임팩트 프레임 (명조식): 화면을 2색(먹 / 종이)으로 끊고, 반전하고, 중심에서 뻗는 만화 집중선을 그립니다.
// URP Full Screen Pass Renderer Feature 가 쓰는 머티리얼 (ProiUltimateScreenFx 가 값을 움직임).
Shader "Proi/ImpactFrame"
{
    Properties
    {
        _Amount ("Amount (0 = 원래 화면)", Range(0, 1)) = 0
        _Invert ("Invert (밝은 곳 -> 먹)", Range(0, 1)) = 1
        _Threshold ("Threshold (밝기 경계)", Range(0, 1)) = 0.35
        _Lines ("Speed lines", Range(0, 1)) = 1
        _LineSeed ("Line seed", Float) = 0
        _Center ("Center (화면 0..1)", Vector) = (0.5, 0.5, 0, 0)
        _Flash ("White flash", Range(0, 1)) = 0
        _Ink ("Ink color", Color) = (0.07, 0.015, 0.01, 1)
        _Paper ("Paper color", Color) = (1, 0.96, 0.9, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ProiImpactFrame"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Amount, _Invert, _Threshold, _Lines, _LineSeed, _Flash;
            float4 _Center;
            half4 _Ink, _Paper;

            float Hash(float n) { return frac(sin(n * 12.9898 + _LineSeed * 78.233) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (_Amount <= 0.001) return half4(col, 1);

                // two tones: bright -> paper, dark -> ink (or the other way round when inverted)
                float lum = dot(col, half3(0.2126, 0.7152, 0.0722));
                float v = smoothstep(_Threshold - 0.03, _Threshold + 0.03, lum);
                v = lerp(v, 1 - v, _Invert);

                // speed lines streaming in from the edges toward the centre, thin and random, flipping the tone
                float2 d = (uv - _Center.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1);
                float r = length(d);
                float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                const float cells = 140;
                float id = floor(a * cells);
                float h = Hash(id);
                float across = abs(frac(a * cells) - 0.5) * 2;                    // 0 on the line's axis
                float width = lerp(0.15, 0.9, Hash(id + 31.7)) * saturate((r - 0.08) * 2.2);   // thicker toward the edge
                float from = lerp(0.12, 0.45, Hash(id + 17.3));
                float streak = step(0.45, h) * step(across, width) * smoothstep(from, from + 0.12, r);
                v = lerp(v, 1 - v, streak * _Lines);

                half3 toned = lerp(_Ink.rgb, _Paper.rgb, v);
                toned = lerp(toned, _Paper.rgb, _Flash);
                return half4(lerp(col, toned, _Amount), 1);
            }
            ENDHLSL
        }
    }
}
