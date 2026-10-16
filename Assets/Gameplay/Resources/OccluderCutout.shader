// Fullscreen passes for AcRoguelike.OccluderCutout (see OccluderCutoutPasses.cs).
Shader "Hidden/AcRoguelike/OccluderCutout"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X_FLOAT(_CutoutSceneDepth);
        TEXTURE2D_X_FLOAT(_CutoutBehindDepth);
        float4 _CutoutFocus;  // xy: circle centre in view space, z: player eye depth, w: radius (m)
        float4 _CutoutParams; // x: inner radius where the fade edge starts, y: floor height, z: blend amount

        float EyeDepth(float rawDepth)
        {
            if (unity_OrthoParams.w == 0)
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            #if UNITY_REVERSED_Z
            rawDepth = 1 - rawDepth;
            #endif
            return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
        }

        float3 WorldPosition(float2 uv, float rawDepth)
        {
            #if !UNITY_REVERSED_Z
            rawDepth = rawDepth * 2 - 1;
            #endif
            return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        }
        ENDHLSL

        Pass
        {
            Name "Snapshot"
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                depth = SAMPLE_TEXTURE2D_X(_CutoutSceneDepth, sampler_PointClamp, input.texcoord).r;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float sceneRaw = SAMPLE_TEXTURE2D_X(_CutoutSceneDepth, sampler_PointClamp, uv).r;
                float behindRaw = SAMPLE_TEXTURE2D_X(_CutoutBehindDepth, sampler_PointClamp, uv).r;

                float3 world = WorldPosition(uv, sceneRaw);
                float3 view = TransformWorldToView(world);
                float depth = -view.z;

                // Occluders are drawn after the snapshot, so only their pixels moved closer than it.
                float occluder = step(EyeDepth(sceneRaw) + .02, EyeDepth(behindRaw))
                    * step(depth, _CutoutFocus.z - .1)  // in front of the player
                    * step(_CutoutParams.y, world.y);   // never the floor around the player

                // Distance from the player on screen, measured in metres at the player's depth.
                float2 offset = unity_OrthoParams.w == 0
                    ? view.xy * (_CutoutFocus.z / max(depth, 1e-3)) - _CutoutFocus.xy
                    : view.xy - _CutoutFocus.xy;
                float circle = 1 - smoothstep(_CutoutParams.x, _CutoutFocus.w, length(offset));

                float alpha = occluder * circle * _CutoutParams.z;
                clip(alpha - 1e-3);
                half3 behind = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                return half4(behind, alpha);
            }
            ENDHLSL
        }
    }
}
