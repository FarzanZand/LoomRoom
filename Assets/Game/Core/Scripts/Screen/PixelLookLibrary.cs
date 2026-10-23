using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Which kind of thing a material is drawn on (Build Materials sorts the pairs by it).
public enum PixelLookCategory
{
    Architecture = 0, // walls, floors, ceilings, dungeon tiles
    Prop         = 1, // furniture, decor, loot, anything else
    Character    = 2, // decided per renderer: anything under a Character or skinned
}

// Pixel lighting: the game's materials listed with their own shader, switched in place to LoomRoom/Pixel Lit
// (ScreenManager > Pixel lighting), with each world's texel size and brightness. Also the older material pairs (a
// Pixel Lit copy per material, Tools > LoomRoom > Pixel Look > Build Materials).
[CreateAssetMenu(menuName = "LoomRoom/Pixel Look Library")]
public class PixelLookLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public Material original;
        public Material pixel;
        [Tooltip("What kind of thing the material is drawn on.")]
        public PixelLookCategory category = PixelLookCategory.Prop;
    }

    [Header("Texels")]
    [Tooltip("Pixel Lit materials give texels per metre at dungeon scale (1 unit = 1 m). The room is built about 22 units to the metre, so its world units get this many metres each. Lower = chunkier texels.")]
    public float roomTexelScale = .03f;
    [Tooltip("Same for the Table player (dungeons and town).")]
    public float tableTexelScale = 1f;

    [Header("Brightness")]
    [Range(.1f, 3f), Tooltip("Multiplies all light on Pixel Lit materials while the Room player is active. 1 = as lit; 1.2 = 20% lighter. Live in Play mode.")]
    public float roomBrightness = 1.2f;
    [Range(.1f, 3f), Tooltip("Same for the Table player (dungeons and town).")]
    public float tableBrightness = 1f;

    [Header("Materials")]
    [Tooltip("Hidden/LoomRoom/Pixel Lit Object (Rendering/PixelLook/PixelLitObject.shader), the per-object pixelation shader. Referenced here so player builds include it.")]
    public Shader objectShader;
    // Editor only: the pairs are read by the editor tools alone, and in a player build they pulled every listed
    // material and its full-size textures in (about 30 MB of Synty sheets nothing draws).
#if UNITY_EDITOR
    [Tooltip("Folder the generated Pixel Lit materials are written to.")]
    [FolderPath] public string outputFolder = "Assets/Game/Rendering/PixelLook/Materials";
    [TableList(AlwaysExpanded = false, ShowPaging = true, NumberOfItemsPerPage = 30)]
    public List<Entry> entries = new();
    [Tooltip("Materials Build Materials leaves alone: they keep drawing as they are. For fine printed artwork, like the board's grid, that stripes when point-sampled.")]
    public List<Material> excluded = new();
#endif


    // ── Pixel lighting ───────────────────────────────────────────────
    // The game's own materials switched between their own shader and LoomRoom/Pixel Lit in place (ScreenManager >
    // Pixel lighting, F6 in Play mode). Filled by Tools > LoomRoom > Pixel Look > Register Pixel Lighting Materials.

    [System.Serializable]
    public class LitEntry
    {
        public Material material;
        public Shader original;
        public int originalQueue = -1;
        public int pixelQueue = -1;
        [Tooltip("Texels per world unit the material shows in game (measured), and the texels across its texture it is drawn with (0 = all of them).")]
        public float texelsPerUnit;
        public float baseTexels;
    }

    [FoldoutGroup("Pixel lighting"), Tooltip("LoomRoom/Pixel Lit.")]
    public Shader pixelLitShader;
    [FoldoutGroup("Pixel lighting/Texel size"), Tooltip("Texels per world unit everything in the dungeons and town is drawn at (the dungeon walls' own density). Finer textures are drawn coarser to match; coarser ones stay as they are. Applied by Tools > LoomRoom > Pixel Look > Even Out Texel Sizes.")]
    public float tableTexelsPerUnit = 10.7f;
    [FoldoutGroup("Pixel lighting/Texel size"), Tooltip("Same for the room, which is built about 22 units to the metre (the apartment walls' density).")]
    public float roomTexelsPerUnit = 1.8f;
    [FoldoutGroup("Pixel lighting/Texel size"), Range(1f, 8f), Tooltip("Characters (enemies, NPCs, the DM, miniatures) get this many times the world's density, so faces stay readable. 1 = same as the walls.")]
    public float characterDetail = 4f;
    [FoldoutGroup("Pixel lighting/Texel size"), Range(1f, 8f), Tooltip("The Table player's own arms and every item (mostly seen in the hand, close up). 1 = same as the walls.")]
    public float firstPersonDetail = 4f;
    [FoldoutGroup("Pixel lighting/Texel size"), Tooltip("Surfaces that keep all their texels, like the walls: the big table's top, seen close up all through the intro and the figures.")]
    public List<Material> keepTexels = new();
    [FoldoutGroup("Pixel lighting"), TableList(AlwaysExpanded = false, ShowPaging = true, NumberOfItemsPerPage = 30)]
    public List<LitEntry> lighting = new();

    static readonly int PixelSizeId = Shader.PropertyToID("_PixelLookPixelSize");
    static readonly int TexelScaleId = Shader.PropertyToID("_PixelLookTexelScale");
    static readonly int RoomTexelScaleId = Shader.PropertyToID("_PixelLookRoomTexelScale");
    static readonly int TableTexelScaleId = Shader.PropertyToID("_PixelLookTableTexelScale");
    static readonly int BrightnessId = Shader.PropertyToID("_PixelLookBrightness");

    // What the Pixel Lit shader reads: pixel size, texel scale per world and brightness for the world being looked at
    // (table: the dungeons and town; otherwise the room). ScreenManager sets it every frame; the main menu once.
    public void ApplyGlobals(bool table, float pixelSize)
    {
        Shader.SetGlobalFloat(PixelSizeId, pixelSize);
        Shader.SetGlobalFloat(TexelScaleId, table ? tableTexelScale : roomTexelScale);
        // Per world, so the room seen from the table (skylights, windows) keeps its own texels.
        Shader.SetGlobalFloat(RoomTexelScaleId, roomTexelScale);
        Shader.SetGlobalFloat(TableTexelScaleId, tableTexelScale);
        Shader.SetGlobalFloat(BrightnessId, table ? tableBrightness : roomBrightness);
    }

    public void SetPixelLighting(bool on)
    {
        if (pixelLitShader == null) return;
        foreach (var e in lighting)
        {
            if (e.material == null || e.original == null) continue;
            var shader = on ? pixelLitShader : e.original;
            if (e.material.shader != shader) e.material.shader = shader;
            e.material.renderQueue = on ? e.pixelQueue : e.originalQueue;
        }
    }
}
