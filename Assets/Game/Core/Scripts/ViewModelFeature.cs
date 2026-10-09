using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// The first-person arms and what they hold (layer ViewModel, set by the ViewModel component) are drawn
// after the world, on a cleared depth buffer, so a wall the player stands against never cuts into them.
// The renderer's own opaque and transparent passes leave that layer out (Desktop Renderer's layer masks).
public class ViewModelFeature : ScriptableRendererFeature
{
    [SerializeField, Tooltip("The view model layer. Leave it out of the renderer's Opaque and Transparent Layer Masks.")]
    LayerMask layer;
    Pass pass;

    public override void Create() => pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (layer.value == 0 || renderingData.cameraData.cameraType != CameraType.Game) return;
        pass.layer = layer;
        renderer.EnqueuePass(pass);
    }

    class Pass : ScriptableRenderPass
    {
        static readonly List<ShaderTagId> Tags = new() { new("UniversalForward"), new("UniversalForwardOnly"), new("SRPDefaultUnlit") };
        public LayerMask layer;

        class Data { public RendererListHandle opaque, transparent; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resources = frameData.Get<UniversalResourceData>();

            using var builder = renderGraph.AddRasterRenderPass<Data>("View Model", out var data);
            var opaque = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            var transparent = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            data.opaque = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, opaque, new FilteringSettings(RenderQueueRange.opaque, layer)));
            data.transparent = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, transparent, new FilteringSettings(RenderQueueRange.transparent, layer)));
            builder.UseRendererList(data.opaque);
            builder.UseRendererList(data.transparent);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture);
            if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture);
            builder.UseAllGlobalTextures(true);
            builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
            {
                // The world's depth is done with (post effects read the depth texture copied earlier).
                ctx.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                ctx.cmd.DrawRendererList(d.opaque);
                ctx.cmd.DrawRendererList(d.transparent);
            });
        }
    }
}
