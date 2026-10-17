Shader "PirateAlliance/BlueFireGate"
{
    Properties
    {
        [HDR] _BaseColor ("Tint and opacity", Color) = (1,1,1,1)
        _GateTime ("Local gate time (seconds)", Float) = 0
        [Enum(Flame,0,Opening,1,GroundGlow,2)] _Mode ("Surface mode", Float) = 0
        _Intensity ("HDR intensity", Range(0,8)) = 2.2
        _Opacity ("Opacity", Range(0,1)) = 1
        _NoiseScale ("Flame detail scale", Range(.35,3)) = 1
        _Seed ("Material seed", Float) = 0
        _EdgeSoftness ("Edge softness", Range(.3,3)) = 1
        _Sway ("Tip movement (metres)", Range(0,.5)) = .14
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BlueFire"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _GateTime;
                float _Mode;
                float _Intensity;
                float _Opacity;
                float _NoiseScale;
                float _Seed;
                float _EdgeSoftness;
                float _Sway;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float seed : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 position = input.positionOS.xyz;
                if (_Mode < .5)
                {
                    float seed = input.uv2.x * 71.0 + _Seed;
                    float h = saturate(input.uv.y);
                    float tip = h * h * _Sway;
                    position.x += (sin(_GateTime * 2.9 + seed + h * 4.7)
                        + .38 * sin(_GateTime * 5.1 + seed * 1.7 + h * 7.3)) * tip;
                    position.z += sin(_GateTime * 2.3 + seed * 1.3 + h * 3.8) * tip * .38;
                }
                output.positionCS = TransformObjectToHClip(position);
                output.uv = input.uv;
                // A ribbon shares one UV2.x seed across its vertices; COLOR.a remains opacity, not randomness.
                output.seed = input.uv2.x * 71.0 + _Seed;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1,0));
                float c = Hash21(cell + float2(0,1));
                float d = Hash21(cell + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }

            float FlameNoise(float2 p)
            {
                // Three octaves only. One additional broad noise sample bends the entire tongue below.
                float n = Noise(p) * .57;
                p = p * 2.03 + float2(7.31, 13.17);
                n += Noise(p) * .28;
                p = p * 2.01 + float2(17.71, 3.43);
                n += Noise(p) * .15;
                return n;
            }

            half4 Flame(Varyings input)
            {
                float2 uv = saturate(input.uv);
                float h = uv.y;
                float x = uv.x * 2.0 - 1.0;
                float t = _GateTime;
                float seed = input.seed;

                // All movement comes from the gate's own clock. Negative Y sampling advects detail upwards.
                float bend = Noise(float2(h * 2.4 - t * .57, seed + 5.13)) - .5;
                float centre = bend * (.055 + h * .56);
                float2 flow = float2((x + centre) * 2.35, h * 4.2 - t * (1.27 + frac(seed) * .28));
                float n = FlameNoise(flow * _NoiseScale + float2(seed * .39, seed * 1.17));

                float width = .86 * (1.0 - pow(h, 1.26));
                float tear = (n - .46) * (.25 + h * .62);
                float field = width - abs(x + centre) + tear;
                float aa = max(fwidth(field) * 1.15, .035 * _EdgeSoftness);
                float silhouette = smoothstep(-aa, aa, field);
                float toeFade = smoothstep(0.0, .11, h);
                float tipFade = 1.0 - smoothstep(.87, 1.0, h);

                // Continuous blue body with ragged edges. Small eddies do not become a sparkling/checkered fill.
                float density = saturate(field / max(.09, width * .84));
                float body = smoothstep(.025, .74, density);
                float core = pow(density, 3.0) * pow(saturate(1.0 - h), .58);
                core *= lerp(.73, 1.0, n);
                half3 cobalt = half3(.006, .028, .38);
                half3 electricBlue = half3(.025, .31, 1.30);
                half3 whiteBlue = half3(1.30, 2.15, 3.00);
                half3 colour = lerp(cobalt, electricBlue, body);
                colour = lerp(colour, whiteBlue, core);

                float breath = .95 + .05 * sin(t * 1.6 + seed * .31);
                float opacity = silhouette * toeFade * tipFade * lerp(.48, .89, body) * breath;
                colour *= _Intensity * _BaseColor.rgb * input.color.rgb;
                opacity *= _Opacity * _BaseColor.a * input.color.a;
                return half4(colour, saturate(opacity));
            }

            half4 Opening(Varyings input)
            {
                float2 p = (input.uv - .5) * 2.0;
                float radius = length(p);
                float t = _GateTime;
                float n = FlameNoise(float2(p.x * 2.1, p.y * 3.3 - t * .38) * _NoiseScale
                    + float2(input.seed * .31, input.seed * .73));
                float bend = Noise(float2(p.y * 2.1 - t * .21, input.seed + 19.7));
                // A torn, smoke-dark opening. The broad irregular inner heat is intentionally not a neon ring.
                float edgeRadius = radius + (n - .5) * .055 + (bend - .5) * .022;
                // Fully occlude the scenery inside the opening; only its ragged edge is translucent.
                float opacity = 1.0 - smoothstep(.90, 1.02, edgeRadius);
                float edgeHeat = smoothstep(.49, .97, radius) * (.14 + .34 * n);
                float innerHeat = pow(saturate(n - .37), 2.0) * .13;
                half3 darkness = half3(.0035, .009, .026);
                half3 colour = darkness + half3(.006, .059, .21) * (edgeHeat + innerHeat) * _Intensity;
                colour *= _BaseColor.rgb * input.color.rgb;
                opacity *= _Opacity * _BaseColor.a * input.color.a;
                return half4(colour, saturate(opacity));
            }

            half4 GroundGlow(Varyings input)
            {
                float2 p = (input.uv - .5) * 2.0;
                float radiusSquared = dot(p,p);
                float falloff = saturate((exp2(-radiusSquared * 4.9) - .025) / .975);
                float breath = .94 + .06 * sin(_GateTime * 1.6 + _Seed * .31);
                // A diffuse reflected pool, without a luminous disk boundary or surface graphics.
                half3 colour = lerp(half3(.004, .027, .24), half3(.045, .34, 1.12), falloff);
                colour *= _Intensity * _BaseColor.rgb * input.color.rgb;
                float opacity = falloff * .44 * breath * _Opacity * _BaseColor.a * input.color.a;
                return half4(colour, saturate(opacity));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_Mode < .5) return Flame(input);
                if (_Mode < 1.5) return Opening(input);
                return GroundGlow(input);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
