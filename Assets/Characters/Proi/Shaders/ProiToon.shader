// Cel-shaded URP shader for Proi: two-tone lighting, soft shadow edge, rim light, inverted-hull outline.
Shader "Proi/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 1)) = 0.5
        _ShadeColor ("Shade Color", Color) = (0.72, 0.58, 0.64, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.45
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.3)) = 0.04
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.35
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.15
        [Header(Hair Highlight)]
        _HairRectA ("Hair UV Rect A (xMin yMin xMax yMax)", Vector) = (0, 0, 0, 0)
        _HairRectB ("Hair UV Rect B (xMin yMin xMax yMax)", Vector) = (0, 0, 0, 0)
        _HairHighlightColor ("Hair Highlight Color", Color) = (1, 0.8, 0.8, 1)
        _HairHighlightStrength ("Hair Highlight Strength", Range(0, 1)) = 0.35
        _HairHighlightSize ("Hair Highlight Size", Range(0.5, 0.99)) = 0.82
        [Header(Outline)]
        _OutlineWidth ("Outline Width (m)", Range(0, 0.01)) = 0.0012
        _OutlinePixels ("Outline Width (screen px, far)", Range(0, 4)) = 1.2
        _OutlineDepthOffset ("Outline Depth Offset (m)", Range(0, 0.02)) = 0.003
        _OutlineColor ("Outline Color", Color) = (0.06, 0.02, 0.03, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _AmbientStrength;
            half4 _RimColor;
            half _RimStrength;
            float4 _HairRectA;
            float4 _HairRectB;
            half4 _HairHighlightColor;
            half _HairHighlightStrength;
            half _HairHighlightSize;
            float _OutlineWidth;
            float _OutlinePixels;
            float _OutlineDepthOffset;
            half4 _OutlineColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                float3 n = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, light.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);
                lit *= smoothstep(0.3h, 0.7h, light.shadowAttenuation);

                half3 col = albedo.rgb * lerp(_ShadeColor.rgb, 1.0h, lit) * light.color;
                col += albedo.rgb * SampleSH(n) * _AmbientStrength;

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // rim on the silhouette (also on the shaded side, weaker) separates the figure from the ground
                half rim = pow(1.0h - saturate(dot(n, viewDir)), 3.0h) * _RimStrength * lerp(0.4h, 1.0h, lit);
                col += _RimColor.rgb * rim;

                // hair: soft highlight band (geometric normal -> one clean band instead of strand noise)
                float2 uv = i.uv;
                half inA = step(_HairRectA.x, uv.x) * step(uv.x, _HairRectA.z) * step(_HairRectA.y, uv.y) * step(uv.y, _HairRectA.w);
                half inB = step(_HairRectB.x, uv.x) * step(uv.x, _HairRectB.z) * step(_HairRectB.y, uv.y) * step(uv.y, _HairRectB.w);
                half hairMask = saturate(inA + inB);
                float3 h = normalize(light.direction + viewDir);
                half ndh = saturate(dot(normalize(i.normalWS), h));
                half band = smoothstep(_HairHighlightSize - 0.04h, _HairHighlightSize + 0.04h, ndh);
                band *= smoothstep(0.35h, 0.7h, normalize(i.normalWS).y);     // top of the head only, not the bangs over the face
                col += _HairHighlightColor.rgb * band * _HairHighlightStrength * hairMask * lit;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;     // R = outline mask (0 on the folded expression overlays)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
                float mask : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                // up close: world-space width that grows a little with distance (as before);
                // far away (quarter view): plus a constant screen-space width so the line stays visible
                float dist = distance(positionWS, GetCameraPositionWS());
                positionWS += normalWS * _OutlineWidth * clamp(dist * 0.5, 0.6, 3.0) * input.color.r;
                // push the hull away from the camera: inner geometry right behind the skin (mouth, jaw)
                // can no longer poke its outline through the face
                float3 positionVS = TransformWorldToView(positionWS);
                positionVS.z -= _OutlineDepthOffset;
                o.positionCS = TransformWViewToHClip(positionVS);
                float2 dirCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float len = length(dirCS);
                float farW = saturate((dist - 3.0) / 5.0);
                if (len > 1e-5)
                    o.positionCS.xy += dirCS / len * (_OutlinePixels * farW * 2.0 / _ScreenParams.xy) * o.positionCS.w * input.color.r;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.mask = input.color.r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.mask - 0.5);       // expression overlays (mask 0): no outline at all, not even their back faces
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float mask : TEXCOORD0;
            };

            ShadowVaryings vert(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                ShadowVaryings o;
                o.positionCS = positionCS;
                o.mask = input.color.r;
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target
            {
                clip(i.mask - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
