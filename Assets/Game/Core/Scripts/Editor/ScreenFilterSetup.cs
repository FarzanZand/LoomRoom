using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Makes one ScreenFilterLook per camera filter in Rendering/Filters with its starting values and lists them on the
// ScreenManager prefab. Safe to run again: a look that exists keeps its values (Overwrite puts the starting values back).
// Ghost uses the co-op ghost's own profile (Multiplayer/Ghost look, the same asset the coop branch's MultiplayerManager uses).
public static class ScreenFilterSetup
{
    const string Folder = "Assets/Game/Rendering/Filters";
    const string Prefab = "Assets/Game/Core/Prefabs/ScreenManager.prefab";
    const string GhostProfile = "Assets/Game/Multiplayer/Ghost look.asset";

    [MenuItem("Tools/LoomRoom/Rendering/Build Screen Filters")]
    public static void BuildMenu() => Debug.Log("[ScreenFilterSetup] " + Build(false));

    [MenuItem("Tools/LoomRoom/Rendering/Build Screen Filters (Overwrite)")]
    public static void OverwriteMenu() => Debug.Log("[ScreenFilterSetup] " + Build(true));

    public static string Build(bool overwrite)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Rendering", "Filters");
        var looks = new List<ScreenFilterLook>();
        int made = 0;
        foreach (ScreenFilter filter in System.Enum.GetValues(typeof(ScreenFilter)))
        {
            if (filter == ScreenFilter.Standard) continue;
            string path = $"{Folder}/{filter}.asset";
            var look = AssetDatabase.LoadAssetAtPath<ScreenFilterLook>(path);
            bool fresh = look == null;
            if (fresh) { look = ScriptableObject.CreateInstance<ScreenFilterLook>(); AssetDatabase.CreateAsset(look, path); made++; }
            if (fresh || overwrite) { Starting(look, filter); EditorUtility.SetDirty(look); }
            looks.Add(look);
        }
        AssetDatabase.SaveAssets();

        var root = PrefabUtility.LoadPrefabContents(Prefab);
        var manager = root.GetComponent<ScreenManager>();
        if (manager == null) { PrefabUtility.UnloadPrefabContents(root); return "no ScreenManager on " + Prefab; }
        manager.filterLooks = looks.ToArray();
        PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        PrefabUtility.UnloadPrefabContents(root);
        // Room's ScreenManager is a plain scene object, not an instance of the prefab: list them there too (save the scene after).
        string scene = "";
        var inScene = Object.FindAnyObjectByType<ScreenManager>(FindObjectsInactive.Include);
        if (inScene != null && !EditorUtility.IsPersistent(inScene))
        {
            Undo.RecordObject(inScene, "Screen filter looks");
            inScene.filterLooks = looks.ToArray();
            EditorSceneManager.MarkSceneDirty(inScene.gameObject.scene);
            scene = $" and on {inScene.gameObject.scene.name}'s ScreenManager (save the scene)";
        }
        return $"{looks.Count} looks in {Folder} ({made} new), listed on {Prefab}{scene}.";
    }

    // A volume profile for a graded look (split toning colours are relative to middle grey, so keep them close to it) (Rendering/Filters/<name> look.asset): split toning, lift and gain, colour, bloom and
    // vignette, done by URP's own post-processing. Made fresh with the look's starting values.
    static VolumeProfile Profile(string name, Color shadows, Color highlights, float balance, Vector4 lift, Vector4 gain,
        float contrast, float saturation, float exposure, Color filter, float bloom, float vignette)
    {
        string path = $"{Folder}/{name} look.asset";
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        T Add<T>() where T : VolumeComponent { var c = profile.Add<T>(true); c.name = typeof(T).Name; c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); return c; }
        var split = Add<SplitToning>(); split.shadows.value = shadows; split.highlights.value = highlights; split.balance.value = balance;
        var lgg = Add<LiftGammaGain>(); lgg.lift.value = lift; lgg.gain.value = gain;
        var grade = Add<ColorAdjustments>(); grade.contrast.value = contrast; grade.saturation.value = saturation; grade.postExposure.value = exposure; grade.colorFilter.value = filter;
        var glow = Add<Bloom>(); glow.intensity.value = bloom; glow.threshold.value = .95f; glow.scatter.value = .6f;
        var edge = Add<Vignette>(); edge.intensity.value = vignette; edge.smoothness.value = .45f; edge.color.value = Color.black;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    // The starting values of each look, tuned against the game's room, dungeon and boss stage.
    static void Starting(ScreenFilterLook l, ScreenFilter filter)
    {
        var blank = ScriptableObject.CreateInstance<ScreenFilterLook>();
        EditorUtility.CopySerialized(blank, l);
        Object.DestroyImmediate(blank);
        l.name = filter.ToString();
        l.filter = filter;
        switch (filter)
        {
            case ScreenFilter.Ghost:
                l.profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GhostProfile);
                break;
            case ScreenFilter.Noir:
                l.colour = ScreenFilterLook.ColourMode.Mono; l.monoDark = new Color(.015f, .015f, .02f); l.monoLight = Color.white;
                l.crush = .04f; l.lift = .85f; l.keepRed = .9f; l.vignette = .35f; l.grain = .2f;
                break;
            case ScreenFilter.Sepia:
                l.colour = ScreenFilterLook.ColourMode.Mono; l.monoDark = new Color(.1f, .06f, .035f); l.monoLight = new Color(.95f, .84f, .66f);
                l.crush = .02f; l.lift = .8f; l.vignette = .5f; l.grain = .3f;
                break;
            case ScreenFilter.Crt:
                l.pixelLines = 360; l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = 1.1f; l.contrast = .05f;
                l.curvature = .06f; l.scanlines = .5f; l.scanlineCount = 360; l.mask = .3f; l.vignette = .25f;
                break;
            case ScreenFilter.Vhs:
            case ScreenFilter.Vhs2:
                l.pixelLines = 400; l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = .85f; l.contrast = -.05f;
                l.colourBleed = 4f; l.wobble = filter == ScreenFilter.Vhs2 ? 0f : .6f; l.grain = .3f; l.vignette = .2f; l.scanlines = .15f; l.scanlineCount = 480;
                break;
            case ScreenFilter.Film:
                l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = .92f; l.contrast = .08f;
                l.lightTint = new Color(1f, .78f, .55f, .15f); l.grain = .28f; l.vignette = .3f;
                break;
            case ScreenFilter.Faded:
                l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = .68f; l.contrast = -.1f; l.blackLift = .07f;
                l.shadowTint = new Color(.45f, .5f, .62f, .25f); l.grain = .12f;
                break;
            case ScreenFilter.Cold:
                l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = .85f; l.contrast = .06f;
                l.shadowTint = new Color(.3f, .5f, .75f, .35f); l.lightTint = new Color(.82f, .9f, 1f, .2f);
                break;
            case ScreenFilter.Warm:
                l.colour = ScreenFilterLook.ColourMode.Grade; l.saturation = 1.06f; l.contrast = .04f;
                l.shadowTint = new Color(.55f, .4f, .32f, .15f); l.lightTint = new Color(1f, .72f, .42f, .3f); l.vignette = .2f;
                break;
            case ScreenFilter.Handheld:
                // Solid fills near each shade, a coarse dither only between them, so shapes read at a glance.
                l.pixelLines = 200; l.colour = ScreenFilterLook.ColourMode.Palette; l.ditherSize = 2; l.ditherScale = 2; l.ditherBand = .45f;
                l.lift = .75f; l.contrast = .35f;
                l.palette = new[] { Hex("#0f380f"), Hex("#306230"), Hex("#8bac0f"), Hex("#9bbc0f") };
                break;
            case ScreenFilter.FourTone:
                l.pixelLines = 270; l.colour = ScreenFilterLook.ColourMode.Palette; l.ditherSize = 2; l.ditherScale = 2; l.ditherBand = .45f;
                l.lift = .75f; l.contrast = .3f;
                l.palette = new[] { Hex("#07080c"), Hex("#2e3a46"), Hex("#4c8f88"), Hex("#e2d9c2") };
                break;
            case ScreenFilter.ClassicOne:
                // Crisp and bright: punchy contrast, richer colour, warm glowing highlights, no tint of its own.
                l.profile = Profile("ClassicOne", new Color(.5f, .5f, .51f), new Color(.56f, .52f, .47f), 0f, new Vector4(1f, 1f, 1f, 0f),
                    new Vector4(1f, 1f, 1f, .04f), contrast: 22f, saturation: 18f, exposure: .4f, filter: Color.white, bloom: .8f, vignette: .25f);
                l.sharpen = .45f;
                break;
            case ScreenFilter.ClassicTwo:
                // Dark and moody: deep contrast, quieter colour, warm light standing out of the dark, no tint of its own.
                l.profile = Profile("ClassicTwo", new Color(.48f, .5f, .53f), new Color(.6f, .52f, .42f), 10f, new Vector4(1f, 1f, 1.01f, .02f),
                    new Vector4(1f, 1f, 1f, .03f), contrast: 25f, saturation: -10f, exposure: .35f, filter: Color.white, bloom: .45f, vignette: .32f);
                l.sharpen = .6f;
                break;
        }
    }
}
