using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Pixel lighting (LoomRoom/Pixel Lit: light evaluated per texture texel and stepped into bands) for the game's own
// materials, with no Pixelator swap or camera filter. Register sets each material's Pixel Lit values once and lists it
// on the Pixel Look library with its own shader, so ScreenManager > Pixel lighting (F6 in Play mode) can flip it
// between the two in place. Covers the game's opaque lit materials (Assets/Game); glass, water, glows, text, UI and
// special shaders are left alone, and so are store assets (Synty and other packs). Run it again after adding materials.
public static class PixelLitConvert
{
    const string PixelLit = "LoomRoom/Pixel Lit";
    const string LibraryPath = "Assets/Game/Rendering/PixelLook/Pixel Look Library.asset";
    static readonly string[] Convertible = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Synty/Generic_Basic", "Universal Render Pipeline/Baked Lit" };

    public static void RegisterMenu() => Debug.Log("[PixelLitConvert] " + Register());

    public static string Register()
    {
        var lib = AssetDatabase.LoadAssetAtPath<PixelLookLibrary>(LibraryPath);
        if (lib == null) return "no library at " + LibraryPath;
        lib.pixelLitShader = Shader.Find(PixelLit);
        var known = new HashSet<Material>(); foreach (var e in lib.lighting) if (e.material != null) known.Add(e.material);
        lib.lighting.RemoveAll(e => e.material == null);
        // Library copies (the Pixelator's own pairs) and excluded materials keep their own shader.
        var skip = new HashSet<Material>(lib.excluded); foreach (var e in lib.entries) if (e.pixel != null) skip.Add(e.pixel);
        int added = 0, skipped = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Game" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith("Assets/Game/Rendering/PixelLook/") || path.Contains("/UI/")) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || known.Contains(m) || skip.Contains(m)) continue;
            var original = m.shader; int queue = m.rawRenderQueue;
            // Made straight on Pixel Lit (the room styles, painted props): the smooth side is URP Lit, which reads the
            // same base map, colour, alpha clip and emission. Without an entry they stayed banded with pixel lighting off.
            if (m.shader.name == PixelLit)
            {
                if (m.renderQueue > 2500) { skipped++; continue; }
                AddSmoothSide(m);
                lib.lighting.Add(new PixelLookLibrary.LitEntry { material = m, original = Shader.Find(SmoothLit), originalQueue = queue, pixelQueue = queue });
                added++;
                continue;
            }
            if (!Convert(m)) { skipped++; continue; }
            lib.lighting.Add(new PixelLookLibrary.LitEntry { material = m, original = original, originalQueue = queue, pixelQueue = m.rawRenderQueue });
            m.shader = original; m.renderQueue = queue; EditorUtility.SetDirty(m);
            added++;
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return $"{added} materials added ({lib.lighting.Count} listed), {skipped} left as they are.";
    }

    // Pixels the same size across a world: each registered material is drawn with as many texels across its texture
    // (_BaseTexels) as give its world's density (the library's Texel size settings) where it is used, measured on every
    // renderer in the scene and in the game's prefabs. Only ever coarser, never sharper than the texture; materials
    // used nowhere it can see (the dungeon walls, built at run time) and non-square textures keep all their texels.
    public static void EvenOutMenu() => Debug.Log("[PixelLitConvert] " + EvenOut());

    public static string EvenOut()
    {
        var lib = AssetDatabase.LoadAssetAtPath<PixelLookLibrary>(LibraryPath);
        if (lib == null) return "no library at " + LibraryPath;
        var entries = new Dictionary<Material, PixelLookLibrary.LitEntry>();
        foreach (var e in lib.lighting) if (e.material != null) entries[e.material] = e;
        var need = new Dictionary<Material, float>(); var density = new Dictionary<Material, List<float>>();

        void Measure(Renderer r, bool room)
        {
            var player = r.GetComponentInParent<Player>(true);
            // Items are mostly seen in the hand, close up, so they count as first person wherever they lie.
            bool firstPerson = (player != null && player.kind == PlayerKind.Table) || r.GetComponentInParent<WorldItem>(true) != null || AssetDatabase.GetAssetPath(r.gameObject).StartsWith("Assets/Game/Items/Prefabs/");
            bool character = !firstPerson && (r is SkinnedMeshRenderer || r.GetComponentInParent<Character>(true) != null);
            float target = (room ? lib.roomTexelsPerUnit : lib.tableTexelsPerUnit) * (firstPerson ? lib.firstPersonDetail : character ? lib.characterDetail : 1f);
            foreach (var m in new HashSet<Material>(r.sharedMaterials))
            {
                if (m == null || !entries.ContainsKey(m)) continue;
                var tex = m.GetTexture("_BaseMap"); if (tex == null) continue;
                float d = PixelRestyle.TexelsPerUnit(r, m); if (d <= 0) continue;
                if (!density.TryGetValue(m, out var list)) density[m] = list = new List<float>();
                list.Add(d);
                // Painted props are drawn at the same pixel size as creatures and items (PixelRestyle.ItemTexelsPerUnit).
                bool paintedProp = !room && !firstPerson && !character && AssetDatabase.GetAssetPath(m).StartsWith(PixelRestyle.Root + "/Props/") && m.name.EndsWith(" painted");
                float texels = tex.width * (paintedProp ? lib.tableTexelsPerUnit * lib.characterDetail : target) / d;
                need[m] = need.TryGetValue(m, out var n) ? Mathf.Max(n, texels) : texels;
            }
        }

        var roots = LightingManager.Instance != null ? LightingManager.Instance.roomRoots : new Transform[0];
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (EditorUtility.IsPersistent(r)) continue;
            bool room = false; foreach (var root in roots) if (root != null && r.transform.IsChildOf(root)) { room = true; break; }
            Measure(r, room);
        }
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Measure(r, false);
        }

        int coarser = 0, kept = 0, nonSquare = 0;
        foreach (var (m, e) in entries)
        {
            var tex = m.GetTexture("_BaseMap");
            float texels = 0;
            if (lib.keepTexels.Contains(m)) { }
            else if (tex != null && tex.width != tex.height) nonSquare++;
            else if (tex != null && need.TryGetValue(m, out var n) && n < tex.width * .9f) texels = Mathf.Max(MinTexels, Mathf.Round(n));
            e.texelsPerUnit = density.TryGetValue(m, out var list) ? Median(list) : 0;
            e.baseTexels = texels;
            if (m.GetFloat("_BaseTexels") != texels) { m.SetFloat("_BaseTexels", texels); EditorUtility.SetDirty(m); }
            if (texels > 0) coarser++; else kept++;
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return $"{coarser} materials drawn coarser to match their world, {kept} keep all their texels ({nonSquare} non-square).";
    }

    // Tiny props keep at least this many texels across their texture, so their parts keep their own colours.
    const float MinTexels = 8f;

    static float Median(List<float> v) { v.Sort(); return v[v.Count / 2]; }

    const string SmoothLit = "Universal Render Pipeline/Lit";

    // What URP Lit needs on a Pixel Lit material to draw it the same way, smooth: matte, and its emission switched on.
    static void AddSmoothSide(Material m)
    {
        Undo.RecordObject(m, "Pixel lighting");
        m.SetFloat("_Smoothness", 0f); m.SetFloat("_Metallic", 0f);
        bool clip = m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f;
        if (clip) m.EnableKeyword("_ALPHATEST_ON");
        var emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
        if (emission.maxColorComponent > 0f) m.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
    }

    public static bool Convert(Material m)
    {
        if (m.shader.name == PixelLit) return false;
        if (System.Array.IndexOf(Convertible, m.shader.name) < 0) return false;
        var n = m.name.ToLowerInvariant();
        if (n.Contains("glass") || n.Contains("water") || n.Contains("eyes") || n.Contains("screen") || n.Contains("glow")) return false;
        bool transparent = (m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f) || m.renderQueue > 2500;
        if (transparent) return false;

        // Read everything before the shader changes.
        Texture baseMap = null; foreach (var p in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" }) if (baseMap == null && m.HasProperty(p)) baseMap = m.GetTexture(p);
        Vector2 scale = Vector2.one, offset = Vector2.zero;
        foreach (var p in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) == baseMap) { scale = m.GetTextureScale(p); offset = m.GetTextureOffset(p); break; }
        Color color = Color.white; foreach (var p in new[] { "_BaseColor", "_Color", "_Color_Tint" }) if (m.HasProperty(p)) { color = m.GetColor(p); break; }
        bool clip = (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f) || m.IsKeywordEnabled("_ALPHATEST_ON") || (m.renderQueue >= 2450 && m.renderQueue <= 2500 && m.shader.name.Contains("Synty"));
        float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : .5f;
        float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
        Texture emissionMap = null; foreach (var p in new[] { "_EmissionMap", "_Emission_Map" }) if (emissionMap == null && m.HasProperty(p)) emissionMap = m.GetTexture(p);
        Color emission = Color.black; bool emissive = m.IsKeywordEnabled("_EMISSION");
        foreach (var p in new[] { "_EmissionColor", "_Emission_Color" }) if (m.HasProperty(p)) { emission = m.GetColor(p); break; }
        if (!emissive && emissionMap == null) emission = Color.black;

        Undo.RecordObject(m, "Pixel lighting");
        m.shader = Shader.Find(PixelLit);
        m.SetTexture("_BaseMap", baseMap); m.SetTextureScale("_BaseMap", scale); m.SetTextureOffset("_BaseMap", offset);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_AlphaClip", clip ? 1 : 0); if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cutoff", cutoff); m.SetFloat("_Cull", cull);
        // Textured: light per texture texel. Untextured: a world grid of texels.
        m.SetFloat("_SnapMode", baseMap != null ? 1 : 2); m.SetFloat("_BaseTexels", 0); m.SetFloat("_TexelsPerUnit", 24);
        m.SetFloat("_DetailStrength", 0); m.SetFloat("_TexelNoise", .015f);
        m.SetFloat("_LightSteps", 8); m.SetFloat("_BandDither", 0); m.SetFloat("_AmbientStrength", 1); m.SetFloat("_Gloss", 0);
        m.SetTexture("_EmissionMap", emissionMap); m.SetColor("_EmissionColor", emission);
        m.renderQueue = clip ? 2450 : -1;
        EditorUtility.SetDirty(m);
        return true;
    }
}
