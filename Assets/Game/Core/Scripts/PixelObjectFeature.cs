using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Per-object pixelation for the Pixelator (on Rendering/Desktop Renderer.asset). Objects whose
// material uses Hidden/LoomRoom/Pixel Lit Object (colour pass tagged LoomPixelObject, skipped by the
// normal render) are drawn into a buffer 1/PixelSize of the camera, then pasted back point-sampled
// with that buffer's depth, so characters and props look low-res inside a full-res scene. Switched by
// PixelLook; does nothing while Enabled is false.
public class PixelObjectFeature : ScriptableRendererFeature
{
    public static bool Enabled;
    public static int PixelSize = 4;

    [SerializeField] Shader compositeShader;
    Material composite;
    Pass pass;

    public override void Create()
    {
        pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Enabled || PixelSize <= 1) return;
        var type = renderingData.cameraData.cameraType;
        if (type != CameraType.Game && type != CameraType.SceneView) return;
        if (composite == null)
        {
            if (compositeShader == null) compositeShader = Shader.Find("Hidden/LoomRoom/Pixel Object Composite");
            if (compositeShader == null) return;
            composite = CoreUtils.CreateEngineMaterial(compositeShader);
        }
        pass.composite = composite;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(composite);

    class Pass : ScriptableRenderPass
    {
        static readonly ShaderTagId Tag = new("LoomPixelObject");
        static readonly int TargetSizeId = Shader.PropertyToID("_PixelObjectTargetSize");
        static readonly int DepthId = Shader.PropertyToID("_PixelObjectDepth");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int SceneDepthId = Shader.PropertyToID("_PixelObjectSceneDepth");
        static readonly int SceneSizeId = Shader.PropertyToID("_PixelObjectSceneSize");
        static readonly int ZTestId = Shader.PropertyToID("_PixelObjectZTest");

        public Material composite;
        readonly MaterialPropertyBlock props = new();

        class DrawData { public RendererListHandle list; public Vector4 size; }
        class CompositeData { public TextureHandle color, depth; public Material material; public MaterialPropertyBlock props; public Vector4 size; }
        class SeedData { public TextureHandle sceneDepth; public Material material; public MaterialPropertyBlock props; public Vector4 sceneSize; }
        readonly MaterialPropertyBlock seedProps = new();

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resources = frameData.Get<UniversalResourceData>();

            var target = cameraData.cameraTargetDescriptor;
            int w = Mathf.Max(1, target.width / PixelSize), h = Mathf.Max(1, target.height / PixelSize);
            var size = new Vector4(w, h, 1f / w, 1f / h);

            var color = renderGraph.CreateTexture(new TextureDesc(w, h)
            {
                name = "_PixelObjectColor", format = GraphicsFormat.R16G16B16A16_SFloat,
                filterMode = FilterMode.Point, clearBuffer = true, clearColor = Color.clear,
            });
            // The scene's depth (from the depth prepass, which leaves out the pixel objects) seeds the
            // buffer's depth, so only what is actually visible is drawn into it.
            bool seed = resources.cameraDepthTexture.IsValid();
            composite.SetFloat(ZTestId, (float)(seed ? CompareFunction.Always : CompareFunction.LessEqual));
            var depth = renderGraph.CreateTexture(new TextureDesc(w, h)
            {
                name = "_PixelObjectDepth", format = GraphicsFormat.D32_SFloat,
                filterMode = FilterMode.Point, clearBuffer = !seed,
            });
            if (seed)
            {
                using var builder = renderGraph.AddRasterRenderPass<SeedData>("Pixel Object Depth Seed", out var data);
                data.sceneDepth = resources.cameraDepthTexture;
                data.material = composite;
                data.props = seedProps;
                data.sceneSize = new Vector4(target.width, target.height, PixelSize, 0);
                builder.UseTexture(resources.cameraDepthTexture);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetRenderFunc(static (SeedData d, RasterGraphContext ctx) =>
                {
                    d.props.SetTexture(SceneDepthId, d.sceneDepth);
                    d.props.SetVector(SceneSizeId, d.sceneSize);
                    d.props.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 1, MeshTopology.Triangles, 3, 1, d.props);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Pixel Objects", out var data))
            {
                var drawing = RenderingUtils.CreateDrawingSettings(Tag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                data.list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, new FilteringSettings(RenderQueueRange.all)));
                data.size = size;
                builder.UseRendererList(data.list);
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, seed ? AccessFlags.ReadWrite : AccessFlags.Write);
                if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture);
                if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture);
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(TargetSizeId, d.size);
                    ctx.cmd.DrawRendererList(d.list);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Pixel Object Composite", out var data))
            {
                data.color = color;
                data.depth = depth;
                data.material = composite;
                data.props = props;
                data.size = size;
                builder.UseTexture(color);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                {
                    d.props.SetTexture(BlitTextureId, ((RTHandle)d.color).rt);
                    d.props.SetTexture(DepthId, ((RTHandle)d.depth).rt);
                    d.props.SetVector(TargetSizeId, d.size);
                    d.props.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.props);
                });
            }
        }
    }
}
