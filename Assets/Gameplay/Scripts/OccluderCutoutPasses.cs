using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AcRoguelike
{
    /// <summary>
    /// Render passes behind <see cref="OccluderCutout"/>:
    /// 1. after the skybox, snapshot colour + depth (occluders sit in a later queue, so they are not in it yet);
    /// 2. after transparents, redraw the transparents into that snapshot (telegraphs and VFX behind a wall stay visible);
    /// 3. blend the snapshot back over occluder pixels that fall inside the circle around the player.
    /// </summary>
    public sealed class OccluderCutoutPasses : IDisposable
    {
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int SceneDepthId = Shader.PropertyToID("_CutoutSceneDepth");
        static readonly int BehindDepthId = Shader.PropertyToID("_CutoutBehindDepth");
        static readonly int FocusId = Shader.PropertyToID("_CutoutFocus");
        static readonly int ParamsId = Shader.PropertyToID("_CutoutParams");

        const int SnapshotShaderPass = 0;
        const int CompositeShaderPass = 1;

        readonly Material material;
        readonly SnapshotPass snapshot;
        readonly TransparentsPass transparents;
        readonly CompositePass composite;
        Vector4 focus, parameters;

        public OccluderCutoutPasses(Shader shader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            snapshot = new SnapshotPass(this);
            transparents = new TransparentsPass();
            composite = new CompositePass(this);
        }

        /// <param name="focusView">Circle centre in view space.</param>
        /// <param name="radius">Circle radius in metres at the focus depth.</param>
        /// <param name="softness">Share of the radius used as fade edge.</param>
        /// <param name="floorY">World height below which nothing is cut.</param>
        /// <param name="amount">0..1 share of the occluder replaced by what is behind it.</param>
        public void Configure(Vector3 focusView, float radius, float softness, float floorY, float amount)
        {
            focus = new Vector4(focusView.x, focusView.y, -focusView.z, radius);
            parameters = new Vector4(Mathf.Clamp(radius * (1 - softness), 0, radius - .001f), floorY, Mathf.Clamp01(amount), 0);
        }

        public void Enqueue(ScriptableRenderer renderer)
        {
            renderer.EnqueuePass(snapshot);
            renderer.EnqueuePass(transparents);
            renderer.EnqueuePass(composite);
        }

        public void Dispose() => CoreUtils.Destroy(material);

        static void DrawFullscreen(RasterCommandBuffer cmd, Material material, int pass, MaterialPropertyBlock block)
        {
            block.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
            cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 1, block);
        }

        sealed class Frame : ContextItem
        {
            public TextureHandle color, depth;

            public override void Reset()
            {
                color = TextureHandle.nullHandle;
                depth = TextureHandle.nullHandle;
            }
        }

        sealed class SnapshotPass : ScriptableRenderPass
        {
            readonly OccluderCutoutPasses owner;
            readonly MaterialPropertyBlock block = new();

            sealed class PassData
            {
                public TextureHandle color, depth;
                public Material material;
                public MaterialPropertyBlock block;
            }

            public SnapshotPass(OccluderCutoutPasses owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var frame = frameData.GetOrCreate<Frame>();
                frame.Reset();
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle color = resources.activeColorTexture, depth = resources.activeDepthTexture;
                if (!color.IsValid() || !depth.IsValid()) return;

                TextureDesc depthDesc = renderGraph.GetTextureDesc(depth);
                // Sampling a multisampled depth buffer needs a resolve this effect does not do.
                if (depthDesc.msaaSamples != MSAASamples.None) return;
                TextureDesc colorDesc = renderGraph.GetTextureDesc(color);
                if (colorDesc.msaaSamples != MSAASamples.None) return;
                colorDesc.name = "_OccluderCutoutColor";
                colorDesc.clearBuffer = false;
                depthDesc.name = "_OccluderCutoutDepth";
                depthDesc.clearBuffer = false;

                frame.color = renderGraph.CreateTexture(colorDesc);
                frame.depth = renderGraph.CreateTexture(depthDesc);

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Occluder Cutout Snapshot", out var data);
                data.color = color;
                data.depth = depth;
                data.material = owner.material;
                data.block = block;
                builder.UseTexture(color);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(frame.color, 0);
                builder.SetRenderAttachmentDepth(frame.depth);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.block.Clear();
                    d.block.SetTexture(BlitTextureId, d.color);
                    d.block.SetTexture(SceneDepthId, d.depth);
                    DrawFullscreen(context.cmd, d.material, SnapshotShaderPass, d.block);
                });
            }
        }

        sealed class TransparentsPass : ScriptableRenderPass
        {
            static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
            };

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public TransparentsPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!frameData.Contains<Frame>()) return;
                var frame = frameData.Get<Frame>();
                if (!frame.color.IsValid()) return;

                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
                // Everything transparent except the occluders themselves.
                var filtering = new FilteringSettings(new RenderQueueRange(OccluderCutout.CutoutQueue + 1, RenderQueueRange.transparent.upperBound));
                var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Occluder Cutout Transparents", out var data);
                data.list = list;
                builder.UseRendererList(list);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(frame.color, 0);
                builder.SetRenderAttachmentDepth(frame.depth);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
            }
        }

        sealed class CompositePass : ScriptableRenderPass
        {
            readonly OccluderCutoutPasses owner;
            readonly MaterialPropertyBlock block = new();

            sealed class PassData
            {
                public TextureHandle behindColor, behindDepth, sceneDepth;
                public Vector4 focus, parameters;
                public Material material;
                public MaterialPropertyBlock block;
            }

            public CompositePass(OccluderCutoutPasses owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!frameData.Contains<Frame>()) return;
                var frame = frameData.Get<Frame>();
                var resources = frameData.Get<UniversalResourceData>();
                if (!frame.color.IsValid() || resources.isActiveTargetBackBuffer) return;

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Occluder Cutout Composite", out var data);
                data.behindColor = frame.color;
                data.behindDepth = frame.depth;
                data.sceneDepth = resources.activeDepthTexture;
                data.focus = owner.focus;
                data.parameters = owner.parameters;
                data.material = owner.material;
                data.block = block;
                builder.UseTexture(frame.color);
                builder.UseTexture(frame.depth);
                builder.UseTexture(resources.activeDepthTexture);
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.block.Clear();
                    d.block.SetTexture(BlitTextureId, d.behindColor);
                    d.block.SetTexture(BehindDepthId, d.behindDepth);
                    d.block.SetTexture(SceneDepthId, d.sceneDepth);
                    d.block.SetVector(FocusId, d.focus);
                    d.block.SetVector(ParamsId, d.parameters);
                    DrawFullscreen(context.cmd, d.material, CompositeShaderPass, d.block);
                });
            }
        }
    }
}
