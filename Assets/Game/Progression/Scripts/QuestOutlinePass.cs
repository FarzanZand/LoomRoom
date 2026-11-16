using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Draws the quest outline (QuestGlow): a soft glow round the silhouette of each place a quest points at, in three
// steps on the game camera before post-processing (Quest Outline.shader: mask, blur, composite), so bloom and the
// screen's look take it like anything else in the picture. Nothing is set up on the renderer asset: while a QuestGlow
// is showing, this adds itself to the game camera's frame (ScriptableRenderer.EnqueuePass from beginCameraRendering),
// and costs nothing while none is.
public class QuestOutlinePass : ScriptableRenderPass
{
    static readonly List<QuestGlow> glows = new();
    static QuestOutlinePass pass;
    static Material template;
    static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture"), BlitScaleBias = Shader.PropertyToID("_BlitScaleBias"),
        QuestMask = Shader.PropertyToID("_QuestMask"), QuestStep = Shader.PropertyToID("_QuestStep"), Spread = Shader.PropertyToID("_Spread");

    public static void Add(QuestGlow glow) { if (glow != null && !glows.Contains(glow)) glows.Add(glow); }
    public static void Remove(QuestGlow glow) => glows.Remove(glow);
    // The look every outline shares (Resources/Quest outline: width, strength, falloff); each QuestGlow draws its mask
    // with a copy of it in its own colour.
    public static Material Template => template != null ? template : template = Resources.Load<Material>("Quest outline");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        glows.Clear(); pass = null; template = null;
        RenderPipelineManager.beginCameraRendering -= OnCamera;
        RenderPipelineManager.beginCameraRendering += OnCamera;
    }

    static void OnCamera(ScriptableRenderContext context, Camera camera)
    {
        if (glows.Count == 0 || camera.cameraType != CameraType.Game || Template == null) return;
        if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) || data.renderType != CameraRenderType.Base) return;
        pass ??= new QuestOutlinePass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        data.scriptableRenderer.EnqueuePass(pass);
    }

    class MaskData { public readonly List<(Renderer renderer, Material material, int parts)> draws = new(); }
    class BlitData { public TextureHandle source, mask; public Material material; public int pass; public Vector4 step; }
    readonly MaterialPropertyBlock block = new();
    static MaterialPropertyBlock shared;

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var target = resources.activeColorTexture;
        var look = Template;
        if (!target.IsValid() || look == null) return;
        shared = block;

        var size = cameraData.cameraTargetDescriptor;
        var full = new TextureDesc(size.width, size.height)
        {
            format = GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = DepthBits.None, msaaSamples = MSAASamples.None,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, clearBuffer = true, clearColor = Color.clear, name = "_QuestMask",
        };
        var mask = renderGraph.CreateTexture(full);
        var half = full; half.width = Mathf.Max(1, size.width / 2); half.height = Mathf.Max(1, size.height / 2);
        half.name = "_QuestGlowA"; var a = renderGraph.CreateTexture(half);
        half.name = "_QuestGlowB"; var b = renderGraph.CreateTexture(half);

        // The pieces, flat in each glow's colour, where they are not hidden.
        using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Quest outline mask", out var data))
        {
            data.draws.Clear();
            for (int i = glows.Count - 1; i >= 0; i--)
            {
                if (glows[i] == null) { glows.RemoveAt(i); continue; }
                glows[i].Collect(data.draws);
            }
            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
            builder.SetRenderFunc(static (MaskData d, RasterGraphContext context) =>
            {
                foreach (var (renderer, material, parts) in d.draws)
                {
                    if (renderer == null || material == null) continue;
                    for (int part = 0; part < parts; part++) context.cmd.DrawRenderer(renderer, material, part, 0);
                }
            });
        }

        // Blurred at half size: across and down, then once more at half the step, which smooths the first's taps. The
        // width is in pixels on a 1080 high screen, so it is the same share of any screen.
        float spread = Mathf.Max(1f, look.GetFloat(Spread)) * size.height / 1080f / 4f;
        Vector4 across = new(spread / size.width, 0f), down = new(0f, spread / size.height);
        Blit(renderGraph, "Quest outline blur 1", mask, a, look, 1, across, TextureHandle.nullHandle);
        Blit(renderGraph, "Quest outline blur 2", a, b, look, 1, down, TextureHandle.nullHandle);
        Blit(renderGraph, "Quest outline blur 3", b, a, look, 1, across * .5f, TextureHandle.nullHandle);
        Blit(renderGraph, "Quest outline blur 4", a, b, look, 1, down * .5f, TextureHandle.nullHandle);
        // Added to the picture, outside the pieces only.
        Blit(renderGraph, "Quest outline", b, target, look, 2, Vector4.zero, mask);
    }

    static void Blit(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination, Material material, int passIndex, Vector4 step, TextureHandle mask)
    {
        using var builder = renderGraph.AddRasterRenderPass<BlitData>(name, out var data);
        data.source = source; data.mask = mask; data.material = material; data.pass = passIndex; data.step = step;
        builder.UseTexture(source);
        if (mask.IsValid()) builder.UseTexture(mask);
        builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
        builder.SetRenderFunc(static (BlitData d, RasterGraphContext context) =>
        {
            RTHandle source = d.source;
            shared.Clear();
            shared.SetTexture(BlitTexture, source);
            if (d.mask.IsValid()) { RTHandle mask = d.mask; shared.SetTexture(QuestMask, mask); }
            shared.SetVector(BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
            shared.SetVector(QuestStep, d.step);
            context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, shared);
        });
    }
}
