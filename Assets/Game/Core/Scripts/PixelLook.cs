using System.Collections.Generic;
using UnityEngine;

public enum PixelatorCamera
{
    Default = 0, // ScreenManager's own room/table effects, untouched
    Clean   = 1, // native resolution, no anti-aliasing blur, the library's clean effects
    LowRes  = 2, // real low-res render with nearest-neighbour upscale, the library's low-res effects
}

public enum PixelLookCategory
{
    Architecture = 0, // walls, floors, ceilings, dungeon tiles
    Prop         = 1, // furniture, decor, loot, anything else
    Character    = 2, // decided per renderer: anything under a Character or skinned
}

// Swaps renderers between their original materials and the Pixel Lit copies in a PixelLookLibrary.
// Owned and ticked by WorldManager (Pixel Look toggles); Play mode only, so scene and prefab files
// never change. Each category can be switched on its own. Renderers spawned later (dungeon floors,
// enemies, loot) are picked up by a periodic rescan.
public class PixelLook
{
    [System.Serializable]
    public struct Parts
    {
        public PixelatorCamera camera;
        public bool architecture, props, characters;
        public int objectPixelSize;                   // per-object pixelation: screen pixels per object pixel, 1 = off
        public bool lowResCharacters, lowResProps;    // which categories get per-object pixelation
        public bool LowRes(PixelLookCategory c) =>
            camera != PixelatorCamera.LowRes && objectPixelSize > 1 &&
            (c == PixelLookCategory.Character ? lowResCharacters : c == PixelLookCategory.Prop && lowResProps);
        public bool Has(PixelLookCategory c) => c switch
        {
            PixelLookCategory.Architecture => architecture,
            PixelLookCategory.Prop => props,
            _ => characters,
        };
    }

    static readonly int TexelScaleId = Shader.PropertyToID("_PixelLookTexelScale");
    const float RescanSeconds = .25f;

    public bool Active { get; private set; }
    public PixelatorCamera CameraMode => Active ? parts.camera : PixelatorCamera.Default;
    public PixelLookLibrary Library { get; private set; }

    readonly List<Material> buffer = new();
    readonly Dictionary<Renderer, bool> isCharacter = new();
    readonly Dictionary<Material, Material> objectTwin = new();     // Pixel Lit material -> its per-object twin
    readonly Dictionary<Material, Material> twinOriginal = new();   // twin -> the game's original material
    Shader objectShader;
    Parts parts;
    float nextScan;

    public void Set(PixelLookLibrary library, bool on, Parts which)
    {
        bool active = on && library != null;
        if (active == Active && library == Library && which.Equals(parts)) return;
        if (Library != null && Library != library) SwapAll(restoreAll: true);
        Library = library;
        Active = active;
        parts = which;
        if (Library != null)
        {
            Library.Rebuild();
            SwapAll(restoreAll: !Active);
            Shader.SetGlobalFloat(TexelScaleId, TexelScale);
        }
        PixelObjectFeature.PixelSize = Mathf.Max(1, parts.objectPixelSize);
        PixelObjectFeature.Enabled = Active && (parts.LowRes(PixelLookCategory.Character) || parts.LowRes(PixelLookCategory.Prop));
        if (ScreenManager.HasInstance) ScreenManager.Instance.ApplyRenderingEffects();
    }

    public void Tick()
    {
        if (!Active || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + RescanSeconds;
        if (isCharacter.Count > 4096) isCharacter.Clear(); // forget destroyed renderers
        Shader.SetGlobalFloat(TexelScaleId, TexelScale);
        SwapAll(restoreAll: false);
    }

    float TexelScale => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
        ? Library.tableTexelScale : Library.roomTexelScale;

    void SwapAll(bool restoreAll)
    {
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            Swap(r, restoreAll);
    }

    void Swap(Renderer r, bool restoreAll)
    {
        if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return;
        if (!isCharacter.TryGetValue(r, out bool character))
            isCharacter[r] = character = r is SkinnedMeshRenderer || r.GetComponentInParent<Character>(true) != null;
        r.GetSharedMaterials(buffer);
        bool changed = false;
        for (int i = 0; i < buffer.Count; i++)
        {
            var m = buffer[i];
            var original = (m != null && twinOriginal.TryGetValue(m, out var o) ? o : null) ?? Library.OriginalFor(m) ?? m;
            var entry = Library.EntryFor(original);
            if (entry == null) continue;
            var category = character ? PixelLookCategory.Character : entry.category;
            var wanted = original;
            if (!restoreAll && Active && parts.Has(category))
                wanted = parts.LowRes(category) ? TwinOf(entry) : entry.pixel;
            if (wanted == m) continue;
            buffer[i] = wanted;
            changed = true;
        }
        if (changed) r.SetSharedMaterials(buffer);
    }

    // The per-object twin: same material on Hidden/LoomRoom/Pixel Lit Object, made once per run.
    Material TwinOf(PixelLookLibrary.Entry entry)
    {
        if (objectTwin.TryGetValue(entry.pixel, out var twin) && twin != null) return twin;
        if (objectShader == null) objectShader = Shader.Find("Hidden/LoomRoom/Pixel Lit Object");
        if (objectShader == null) return entry.pixel;
        twin = new Material(entry.pixel) { shader = objectShader, name = entry.pixel.name + " (object)" };
        twin.renderQueue = entry.pixel.renderQueue;
        objectTwin[entry.pixel] = twin;
        twinOriginal[twin] = entry.original;
        return twin;
    }
}
