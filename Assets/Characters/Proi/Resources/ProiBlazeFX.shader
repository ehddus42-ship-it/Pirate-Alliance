// Blender flame-combo VFX (D:/Blender_Test/VFX_Scripts/fire_vfx_lib.py) ported to URP. Additive HDR flames that bloom.
// The node graphs of the Blender materials are reproduced 1:1 (same formulas / constants), with an fBm gradient
// noise standing in for Blender's 4D Noise Texture. Meshes: Resources/Proi_BlazeFX.fbx (UV0 = Blender attributes).
//   _MODE_FLAME    teardrop shells / sleeves / impact flare:  val = noise + Intensity - T*TailFall - facing*Erode
//   _MODE_SPIRAL   ribbons round the arm: revealed from Head, burnt from Tail
//   _MODE_LINES    speed lines (u along the streak)
//   _MODE_RING     shockwave disc (radius 1 in mesh space)
//   _MODE_FLASH    contact flash sphere
//   _MODE_CRESCENT kick arc ribbon built at runtime (u = time along the arc, v = 0 inner .. 1 outer)
Shader "Proi/BlazeFX"
{
    Properties
    {
        _Ramp ("Colour ramps (rows: body, core, deep, flash)", 2D) = "white" {}
        _RampRow ("Ramp row", Float) = 0
        _Strength ("Strength (HDR)", Float) = 3
        _Intensity ("Flame intensity", Float) = 0
        _TailFall ("Tail fall-off", Float) = 0.95
        _Erode ("Edge erosion", Float) = 0.55
        _FacingPow ("Facing power (Layer Weight blend)", Float) = 0.9
        _NoiseScale ("Noise scale (along, across, overall)", Vector) = (5, 11, 1, 0)
        _Scroll ("Scroll per frame (along, up)", Vector) = (0.9, 0.15, 0, 0)
        _WSpeed ("Evolution per frame", Float) = 0.06
        _Detail ("Noise octaves", Float) = 4
        _Distort ("Noise distortion", Float) = 0.3
        _Seed ("Seed", Float) = 0
        _AxisOS ("Flame axis (mesh space, front)", Vector) = (-1, 0, 0, 0)
        _UpOS ("Up (mesh space)", Vector) = (0, 0, 1, 0)
        _Wobble ("Silhouette wobble (mesh units)", Float) = 0
        _Head ("Head", Float) = 0
        _Tail ("Tail", Float) = 0
        _Fade ("Fade", Float) = 0
        _Life ("Life (u units)", Float) = 0.5
        _Hold ("Hold before burning (u units)", Float) = 0
        _Edge ("Across edge (v from, v to)", Vector) = (0, 0.7, 0, 0)
        _Brightness ("Global brightness", Float) = 1
        [KeywordEnum(Flame, Spiral, Lines, Ring, Flash, Crescent)] _Mode ("Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BlazeFX"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _MODE_FLAME _MODE_SPIRAL _MODE_LINES _MODE_RING _MODE_FLASH _MODE_CRESCENT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ProiBlazeNoise.hlsl"

            TEXTURE2D(_Ramp); SAMPLER(sampler_Ramp);
            CBUFFER_START(UnityPerMaterial)
                float4 _Ramp_ST;
                float _RampRow, _Strength, _Intensity, _TailFall, _Erode, _FacingPow, _WSpeed, _Detail, _Distort, _Seed;
                float4 _NoiseScale, _Scroll, _AxisOS, _UpOS, _Edge;
                float _Wobble, _Head, _Tail, _Fade, _Life, _Hold, _Brightness, _Mode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posOS : TEXCOORD1;
                float3 nWS : TEXCOORD2;
                float3 vWS : TEXCOORD3;
            };

            float Frame() { return _Time.y * 30.0; }          // Blender curves are keyed per frame at 30 fps

            float3 Ramp(float t)
            {
                return SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(saturate(t), (_RampRow + 0.5) * 0.25)).rgb;
            }

            // Blender Mapping (scale along / across the flame axis, then scroll) followed by Noise Texture (scale, W)
            float FlameNoise(float3 p)
            {
                float3 ax = normalize(_AxisOS.xyz), up = normalize(_UpOS.xyz);
                float along = dot(p, ax);
                float3 q = ax * along * _NoiseScale.x + (p - ax * along) * _NoiseScale.y;
                q += (ax * _Scroll.x + up * _Scroll.y) * Frame();
                return BlazeNoise4(q * _NoiseScale.z, _Seed + _WSpeed * Frame(), _Detail, 0.55, _Distort);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 p = input.positionOS.xyz;
            #if defined(_MODE_FLAME)
                // Displace modifier: clouds texture (size 0.06) in the space of an empty drifting along the flame
                float3 ax = normalize(_AxisOS.xyz), up = normalize(_UpOS.xyz);
                float3 c = p - (ax * 0.035 - up * 0.01) * Frame();
                float n = BlazeNoise4(c / 0.06, _Seed, 2, 0.5, 0);
                p += normalize(input.normalOS) * (n - 0.5) * _Wobble;
            #endif
                float3 pWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(pWS);
                o.posOS = p;
                o.nWS = TransformObjectToWorldNormal(input.normalOS);
                o.vWS = GetWorldSpaceViewDir(pWS);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float ndv = abs(dot(normalize(i.nWS), normalize(i.vWS)));
                float facing = pow(saturate(1.0 - ndv), _FacingPow);        // Blender Layer Weight "Facing"
                float3 col = 0;

            #if defined(_MODE_FLAME)
                float val = FlameNoise(i.posOS) + _Intensity - i.uv.x * _TailFall - facing * _Erode;
                float alpha = smoothstep(0.42, 0.58, val);
                col = Ramp((val - 0.42) / (1.15 - 0.42)) * alpha * _Strength;

            #elif defined(_MODE_SPIRAL)
                float U = i.uv.x, V = i.uv.y;
                float mHead = 1.0 - smoothstep(-0.12, 0.0, U - _Head);
                float mTail = smoothstep(0.0, 0.25, U - _Tail);
                float across = pow(saturate(1.0 - abs(V * 2.0 - 1.0)), 0.8);
                float val = mHead * mTail * across * 0.7 + FlameNoise(i.posOS) * 0.9 - (U * 0.45 + 0.12);
                float alpha = smoothstep(0.45, 0.62, val);
                col = Ramp((val - 0.45) / 0.55) * alpha * _Strength;

            #elif defined(_MODE_LINES)
                col = float3(1.0, 0.55, 0.2) * pow(saturate(1.0 - i.uv.x), 1.5) * _Fade * _Strength;

            #elif defined(_MODE_RING)
                float d = length(i.posOS);
                float ring = smoothstep(0.72, 0.93, d) * (1.0 - smoothstep(0.93, 1.0, d));
                float n = BlazeNoise4(i.posOS * 3.0 * 2.5, _Seed + 0.15 * Frame(), 3, 0.55, 0.3);
                float val = ring * lerp(0.15, 1.0, smoothstep(0.35, 0.6, n)) * _Fade;
                col = Ramp(val) * val * _Strength;

            #elif defined(_MODE_FLASH)
                float g = pow(saturate(1.0 - facing), 2.5);
                col = Ramp(g) * g * _Fade * _Strength;

            #elif defined(_MODE_CRESCENT)
                float U = i.uv.x, V = i.uv.y;
                float3 q = float3(U * 14.0 + _Scroll.x * Frame(), V * 2.5, 0.0);
                float n = BlazeNoise4(q, _Seed + _WSpeed * Frame(), 5, 0.55, 0.5);
                n = saturate(0.5 + (n - 0.5) * 1.6);         // Blender's noise has more contrast at 5 octaves
                float age = _Head - U;
                float live = smoothstep(-0.03, 0.01, age) * (1.0 - smoothstep(_Hold, _Hold + _Life, age));
                float across = smoothstep(_Edge.x, _Edge.y, V) * lerp(1.0, 0.25, smoothstep(0.9, 1.0, V));
                float val = across * 0.55 + n * 0.8 - ((1.0 - live) * 1.1 + 0.2);
                float alpha = smoothstep(0.42, 0.6, val);
                col = Ramp((val - 0.42) / 0.63) * alpha * _Strength;
            #endif

                return half4(col * _Brightness, 0);
            }
            ENDHLSL
        }
    }
}
