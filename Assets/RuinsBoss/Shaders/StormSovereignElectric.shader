Shader "PirateAlliance/StormSovereignElectric"
{
    Properties
    {
        [MainTexture] _BaseMap("Meshy face and body texture", 2D) = "white" {}
        [MainColor] _BaseColor("Body tint", Color) = (1, 1, 1, 1)
        _Opacity("Body opacity", Range(0, 1)) = 0.40
        [HDR] _EmissionColor("Electric light", Color) = (1.8, 3.5, 4.5, 1)
        _RimPower("Electric rim sharpness", Range(1, 8)) = 3.8
        _CrackleStrength("Broken electric strokes", Range(0, 3)) = 1.25
        [HideInInspector] _ColorMask("Color write mask", Float) = 15
        [HideInInspector] _ZWrite("Depth write", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ElectricBody"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Opacity;
                half _RimPower;
                half _CrackleStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float Hash31(float3 seed)
            {
                seed = frac(seed * 0.1031);
                seed += dot(seed, seed.yzx + 33.33);
                return frac((seed.x + seed.y) * seed.z);
            }

            // Fixed angular paths switch on in discrete bursts. Their coordinates never scroll.
            float BrokenStroke(float2 position, float tick, float salt)
            {
                float2 cell = floor(position);
                float2 local = frac(position);
                float segment = min(3.0, floor(local.y * 4.0));
                float segmentT = frac(local.y * 4.0);
                float start = Hash31(float3(cell, segment + salt));
                float end = Hash31(float3(cell, segment + salt + 1.0));
                float pathX = 0.5 + (lerp(start, end, segmentT) - 0.5) * 0.66;
                float distanceToPath = abs(local.x - pathX);
                float aa = max(fwidth(distanceToPath), 0.006);
                float filament = 1.0 - smoothstep(0.012, 0.024 + aa, distanceToPath);
                float endFade = smoothstep(0.04, 0.13, local.y) * (1.0 - smoothstep(0.84, 0.96, local.y));
                float flash = Hash31(float3(cell + salt, tick));
                float gate = step(0.68, flash);
                return filament * endFade * gate * lerp(0.65, 1.0, flash);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = pow(1.0h - saturate(dot(normalWS, viewWS)), max(1.0h, _RimPower));
                half luminance = dot(source.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half maximum = max(source.r, max(source.g, source.b));
                half minimum = min(source.r, min(source.g, source.b));
                half veins = smoothstep(0.32h, 0.90h, maximum) * saturate((maximum - minimum) * 1.8h + 0.15h);

                // URP supplies scaled Time.time in PlayMode, so pause also freezes every flash.
                float tick = floor(_Time.y * 16.0);
                float3 p = input.positionOS * 3.6;
                float stroke = BrokenStroke(float2(p.x + p.z * 0.61, p.y), tick, 9.4);
                stroke = max(stroke, BrokenStroke(float2(p.z - p.x * 0.43, p.y * 1.29 + p.x * 0.27), tick, 27.8) * 0.72);
                half crackle = (half)stroke * _CrackleStrength;
                half rimPulse = 0.94h + 0.06h * (half)Hash31(float3(tick, 4.7, 2.3));

                // Retain the authored face and hair values with matte form shading, without wet specular highlights.
                half form = 0.78h + 0.22h * saturate(dot(normalWS, normalize(half3(-0.35h, 0.8h, 0.45h))));
                half3 color = source.rgb * _BaseColor.rgb * form;
                half peak = max(_EmissionColor.r, max(_EmissionColor.g, _EmissionColor.b));
                half3 whiteElectric = lerp(_EmissionColor.rgb, half3(peak, peak, peak), 0.60h);
                color += _EmissionColor.rgb * veins * 0.14h;
                color += whiteElectric * (rim * rimPulse * 0.78h + crackle * 1.35h);

                // The broad body stays translucent; small bright strokes and the silhouette remain readable.
                half alpha = _Opacity * (0.90h + 0.16h * saturate(luminance) + 0.70h * rim + 0.50h * saturate(crackle) + 0.15h * veins);
                alpha = min(0.92h, alpha) * source.a * _BaseColor.a;
                return half4(MixFog(color, input.fogFactor), saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
