using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools > LoomRoom > Pixel Look: finds every opaque world material the game uses (scene, prefabs and
// data under Assets/Game), writes a LoomRoom/Pixel Lit copy of each and records the pair in the
// PixelLookLibrary. The originals are never touched. Build keeps your tuning on existing copies and only
// refreshes what comes from the original (texture, colour, cutoff); Reset Tuning re-applies the rules.
public static class PixelLookBuilder
{
    const string Root = "Assets/Game/Rendering/PixelLook";
    const string LibraryPath = Root + "/Pixel Look Library.asset";
    const string PatternFolder = Root + "/Patterns";
    const string ShaderName = "LoomRoom/Pixel Lit";

    static readonly string[] SourceShaders =
    {
        "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
        "Synty/Generic_Basic", "Synty/Generic_Standard",
    };

    // Name keyword -> pattern, first match wins.
    static readonly (string[] keys, string pattern, float strength)[] PatternRules =
    {
        (new[] { "brick" }, "Stone bricks", .8f),
        (new[] { "stone", "crypt", "vault", "sandstone", "slate" }, "Large blocks", .7f),
        (new[] { "flag", "cobble", "tile" }, "Flagstones", .7f),
        (new[] { "wood", "walnut", "oak", "timber", "trim", "plank" }, "Wood planks", .6f),
        (new[] { "linen", "cloth", "fabric", "rug", "carpet", "curtain", "bed" }, "Cloth", .6f),
        (new[] { "brass", "bronze", "iron", "pewter", "metal", "steel", "gold" }, "Metal", .5f),
        (new[] { "fur", "rat" }, "Fur", .6f),
        (new[] { "leaf", "leaves", "forest", "plant", "grass", "foliage" }, "Leaves", .6f),
        (new[] { "roof", "shingle", "thatch" }, "Shingles", .7f),
        (new[] { "dirt", "mud", "ground", "terracotta" }, "Dirt", .6f),
        (new[] { "skin", "slime", "jelly" }, "Skin", .5f),
        (new[] { "plaster", "wall", "white", "limestone", "ceiling" }, "Plaster", .7f),
    };

    [MenuItem("Tools/LoomRoom/Pixel Look/Build Materials")]
    public static void Build() => Run(false);

    [MenuItem("Tools/LoomRoom/Pixel Look/Build Materials (Reset Tuning)")]
    public static void BuildReset()
    {
        if (EditorUtility.DisplayDialog("Pixel Look", "Re-apply the default rules to every Pixel Lit material? Hand tuning on them is lost.", "Reset", "Cancel"))
            Run(true);
    }

    [MenuItem("Tools/LoomRoom/Pixel Look/Select Library")]
    public static void SelectLibrary() => Selection.activeObject = LoadOrCreateLibrary();

    public static string Run(bool resetTuning)
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogError("Pixel Look: shader " + ShaderName + " not found."); return "no shader"; }
        ImportPatterns();
        var library = LoadOrCreateLibrary();
        Directory.CreateDirectory(library.outputFolder);

        var existing = library.entries.Where(e => e.original != null && e.pixel != null)
                                      .GroupBy(e => e.original).ToDictionary(g => g.Key, g => g.First());
        var usedNames = new HashSet<string>(existing.Values.Select(e => e.pixel.name));
        int created = 0, updated = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var original in SourceMaterials())
            {
                Material pixel;
                if (!existing.TryGetValue(original, out var entry))
                {
                    pixel = new Material(shader) { name = UniqueName(original.name, usedNames) };
                    AssetDatabase.CreateAsset(pixel, $"{library.outputFolder}/{pixel.name}.mat");
                    existing[original] = new PixelLookLibrary.Entry { original = original, pixel = pixel, category = CategoryFor(original) };
                    ApplyRules(original, pixel);
                    created++;
                }
                else
                {
                    pixel = entry.pixel;
                    if (pixel.shader != shader) pixel.shader = shader;
                    if (resetTuning) { ApplyRules(original, pixel); entry.category = CategoryFor(original); }
                    updated++;
                }
                CopyBase(original, pixel);
                EditorUtility.SetDirty(pixel);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        library.entries = existing.Values.OrderBy(e => e.category).ThenBy(e => e.pixel.name).ToList();
        library.Rebuild();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        if (WorldManager.HasInstance && WorldManager.Instance.pixelLookLibrary == null)
        {
            Undo.RecordObject(WorldManager.Instance, "Assign Pixel Look Library");
            WorldManager.Instance.pixelLookLibrary = library;
            EditorUtility.SetDirty(WorldManager.Instance);
        }
        string report = $"Pixel Look: {created} created, {updated} kept, {library.entries.Count} pairs.";
        Debug.Log(report, library);
        return report;
    }

    static PixelLookLibrary LoadOrCreateLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<PixelLookLibrary>(LibraryPath);
        if (library != null) return library;
        Directory.CreateDirectory(Root);
        library = ScriptableObject.CreateInstance<PixelLookLibrary>();
        AssetDatabase.CreateAsset(library, LibraryPath);
        return library;
    }

    // Every opaque material reachable from the game's scene, prefabs and data, including ones
    // embedded in model files.
    static IEnumerable<Material> SourceMaterials()
    {
        var roots = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab t:ScriptableObject t:Scene", new[] { "Assets/Game" }))
            roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        var seen = new HashSet<Material>();
        foreach (var path in AssetDatabase.GetDependencies(roots.ToArray(), true))
        {
            if (path.StartsWith(Root)) continue;
            IEnumerable<Material> mats = path.EndsWith(".mat")
                ? new[] { AssetDatabase.LoadAssetAtPath<Material>(path) }
                : path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase)
                    ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                    : Enumerable.Empty<Material>();
            foreach (var m in mats)
                if (m != null && seen.Add(m) && IsSource(m)) yield return m;
        }
    }

    static bool IsSource(Material m)
    {
        if (!SourceShaders.Contains(m.shader.name)) return false;
        string n = m.name.ToLowerInvariant();
        return !(n.Contains("glass") || n.Contains("water") || n.Contains("window") || n.Contains("screen"));
    }

    static readonly string[] ArchitectureKeys =
    {
        "wall", "ceiling", "floor", "skylight", "trim", "plaster", "limestone", "warm white",
        "stone", "flagstone", "slate", "timber", "vault", "sandstone", "generic_wood", "lit",
    };

    // Architecture = the walls, floors and ceilings of the room and of dungeon floors. Characters are
    // decided per renderer at runtime, so everything else is a prop.
    static PixelLookCategory CategoryFor(Material m)
    {
        string path = AssetDatabase.GetAssetPath(m);
        string name = m.name.ToLowerInvariant();
        if (path.Contains("/Characters/")) return PixelLookCategory.Character;
        if (path.Contains("/Styles/") || ArchitectureKeys.Any(k => name == k || name.Contains(k + " ") || name.Contains(" " + k) || name.StartsWith(k)))
            return PixelLookCategory.Architecture;
        return PixelLookCategory.Prop;
    }

    static string UniqueName(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int i = 2; !used.Add(candidate); i++) candidate = $"{name} {i}";
        return candidate;
    }

    static Texture BaseTexture(Material m)
    {
        foreach (var p in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" })
            if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
        return null;
    }

    static string BaseTextureProperty(Material m)
    {
        foreach (var p in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" })
            if (m.HasProperty(p) && m.GetTexture(p) != null) return p;
        return null;
    }

    // What always follows the original: texture, tiling, colour, cutout, culling, emission.
    static void CopyBase(Material src, Material dst)
    {
        var prop = BaseTextureProperty(src);
        dst.SetTexture("_BaseMap", prop != null ? src.GetTexture(prop) : null);
        if (prop != null)
        {
            dst.SetTextureScale("_BaseMap", src.GetTextureScale(prop));
            dst.SetTextureOffset("_BaseMap", src.GetTextureOffset(prop));
        }
        var color = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                  : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
        dst.SetColor("_BaseColor", color);

        bool clip = src.IsKeywordEnabled("_ALPHATEST_ON") || (src.HasProperty("_AlphaClip") && src.GetFloat("_AlphaClip") > .5f);
        dst.SetFloat("_AlphaClip", clip ? 1 : 0);
        if (clip) dst.EnableKeyword("_ALPHATEST_ON"); else dst.DisableKeyword("_ALPHATEST_ON");
        dst.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff")
                              : src.HasProperty("_Alpha_Clip_Threshold") ? src.GetFloat("_Alpha_Clip_Threshold") : .5f);
        dst.SetFloat("_Cull", src.HasProperty("_Cull") ? src.GetFloat("_Cull") : 2);

        bool emissive = src.IsKeywordEnabled("_EMISSION") || (src.HasProperty("_Enable_Emission") && src.GetFloat("_Enable_Emission") > .5f);
        Color emission = Color.black;
        Texture emissionMap = null;
        if (emissive)
        {
            emission = src.HasProperty("_EmissionColor") ? src.GetColor("_EmissionColor")
                     : src.HasProperty("_Emission_Color") ? src.GetColor("_Emission_Color") : Color.black;
            emissionMap = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap")
                        : src.HasProperty("_Emission_Map") ? src.GetTexture("_Emission_Map") : null;
        }
        dst.SetColor("_EmissionColor", emission);
        dst.SetTexture("_EmissionMap", emissionMap);
        // See-through materials become an ordered screen-door dither in the opaque pass.
        bool transparent = src.HasProperty("_Surface") && src.GetFloat("_Surface") > .5f;
        dst.SetFloat("_DitherAlpha", transparent ? 1 : 0);
        dst.renderQueue = clip || transparent ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
    }

    // Default look per material: how texels are found, which pattern, gloss.
    static void ApplyRules(Material src, Material dst)
    {
        string path = AssetDatabase.GetAssetPath(src);
        string name = src.name.ToLowerInvariant();
        var tex = BaseTexture(src);
        bool moves = path.Contains("/Characters/") || path.Contains("/Items/") || path.Contains("/Combat/");
        bool atlas = tex != null && tex.width > 256 && (name.Contains("polygon") || name.StartsWith("generic_0") || name.Contains("gothic"));

        dst.SetFloat("_LightSteps", 8);
        dst.SetFloat("_BandDither", 0);
        dst.SetFloat("_AmbientStrength", 1);
        dst.SetColor("_ShadeColor", new Color(.86f, .88f, 1f));
        dst.SetFloat("_DetailStrength", 0);
        dst.SetTexture("_DetailMap", null);
        dst.SetFloat("_TexelsPerUnit", 24);
        dst.SetFloat("_BaseTexels", 0);

        if (tex != null && !atlas)
        {
            // A real texture: its own texels are the pixels. Big ones are brought down to 64 across.
            dst.SetFloat("_SnapMode", 1);
            dst.SetFloat("_BaseTexels", tex.width > 256 ? (path.Contains("/Paintings/") ? 64 : 32) : 0);
            dst.SetFloat("_TexelNoise", .015f);
        }
        else
        {
            // Flat colour or a Synty colour atlas: a texel grid plus a pattern from the name.
            dst.SetFloat("_SnapMode", moves || atlas ? 3 : 2);
            dst.SetFloat("_TexelNoise", .03f);
            foreach (var (keys, pattern, strength) in PatternRules)
            {
                if (!keys.Any(name.Contains)) continue;
                dst.SetTexture("_DetailMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{PatternFolder}/{pattern}.png"));
                dst.SetFloat("_DetailStrength", strength);
                break;
            }
        }

        float metallic = src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0;
        bool shiny = metallic > .5f || name.Contains("brass") || name.Contains("bronze") || name.Contains("iron") || name.Contains("pewter");
        dst.SetFloat("_Gloss", shiny ? .6f : 0);
        dst.SetFloat("_GlossSize", .05f);
    }

    static void ImportPatterns()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PatternFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
            if (ti.filterMode == FilterMode.Point && !ti.sRGBTexture && ti.textureCompression == TextureImporterCompression.Uncompressed) continue;
            ti.filterMode = FilterMode.Point;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
    }
}
