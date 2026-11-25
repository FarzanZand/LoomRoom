using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Draws what must keep its own colours under the camera filter (noir, VHS...): materials whose shader has a pass tagged
// LoomAfterFilter (LoomRoom/After Filter Particle: the Pianist's notes). The renderer's own passes never draw that tag;
// this pass draws it right after the Retro Screen pass (the camera filter) and before the HUD. The shader hides what
// lies behind the world with the scene's depth texture, and the depth buffer still holds the first-person arms.
// Added to every game camera as it starts rendering (no renderer asset change), from the first scene on.
public static class AfterFilterPass
{
    static Pass pass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { RenderPipelineManager.beginCameraRendering -= Add; pass = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Listen() { RenderPipelineManager.beginCameraRendering -= Add; RenderPipelineManager.beginCameraRendering += Add; }

    static void Add(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return;
        if (data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
        pass ??= new Pass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1 };
        data.scriptableRenderer.EnqueuePass(pass);
    }

    class Pass : ScriptableRenderPass
    {
        static readonly List<ShaderTagId> Tags = new() { new("LoomAfterFilter") };
        class Data { public RendererListHandle list; }

        public Pass() => ConfigureInput(ScriptableRenderPassInput.Depth);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraDepthTexture.IsValid()) return;

            using var builder = renderGraph.AddRasterRenderPass<Data>("After Filter", out var data);
            var drawing = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            data.list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, new FilteringSettings(RenderQueueRange.all)));
            builder.UseRendererList(data.list);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.UseTexture(resources.cameraDepthTexture);
            builder.UseAllGlobalTextures(true);
            builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
        }
    }
}
