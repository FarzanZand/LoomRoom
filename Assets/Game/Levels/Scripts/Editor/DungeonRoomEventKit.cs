using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// The pieces the room events and the newer hand-built rooms use, as prefabs in Levels/Dungeon1/Prefabs:
//   Traps/Spike trap, Traps/Ambush gate      (Blender models from Tools/trap_models.py, Traps/Models)
//   Features/Shrine                          (the altar table with a skull pile and candles; DungeonShrine)
//   Furnishing/Sarcophagus, Skull pile, Pew, Candle rack, Hanging chain, Candle cluster
//   Props/Effects/Moonlight                  (a cold spot light, a shaft of dust and motes for moonlit rooms)
// and wires them on Table level defaults (Room events tab, every level). Only missing prefabs are made (Overwrite rebuilds); the
// Synty models are copied in as plain objects and painted with the painted-prop pipeline afterwards (loomroom-art).
public static class DungeonRoomEventKit
{
    const string Prefabs = "Assets/Game/Levels/Dungeon1/Prefabs";
    const string Synty = "Assets/Synty/PolygonDarkFantasy/Prefabs/Props";
    const string Library = "Assets/Game/Audio/Library/_Sound Effects Library";
    static bool overwrite;

    public static string Run() => Run(false);
    public static string RunOverwrite() => Run(true);

    public static string Run(bool replace)
    {
        overwrite = replace;
        if (!AssetDatabase.IsValidFolder($"{Prefabs}/Traps")) AssetDatabase.CreateFolder(Prefabs, "Traps");
        Readable($"{Prefabs}/Traps/Models/Spike trap.fbx");
        Readable($"{Prefabs}/Traps/Models/Ambush gate.fbx");
        Readable($"{Prefabs}/Traps/Models/Moon shaft.fbx");
        var made = new List<string>();
        void Note(GameObject g) { if (g != null) made.Add(g.name); }
        Note(SpikeTrap()); Note(AmbushGate());
        Note(Furnishing("Sarcophagus", "SM_Prop_Tomb_01", 1f));
        Note(Furnishing("Skull pile", "SM_Prop_Skull_Pile_01", 1f));
        Note(Furnishing("Pew", "SM_Prop_Pew_01", 1f, new Vector3(1f, 0f, 0f)));
        Note(CandleRack()); Note(HangingChain()); Note(CandleCluster());
        Note(Shrine()); Note(Moonlight());
        Wire();
        AssetDatabase.SaveAssets();
        return "room event props: " + string.Join(", ", made);
    }

    // Rebuilds only the moonlight (its material and prefab keep their asset ids).
    public static string RebuildMoonlight()
    {
        overwrite = true;
        try { return Moonlight().name; } finally { overwrite = false; }
    }

    static void Readable(string fbx)
    {
        if (AssetImporter.GetAtPath(fbx) is not ModelImporter importer) return;
        if (importer.isReadable && importer.materialImportMode == ModelImporterMaterialImportMode.None) return;
        importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
    }

    static bool Skip(string path, out GameObject existing)
    {
        existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return existing != null && !overwrite;
    }

    static GameObject Save(GameObject root, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material Flat(string name, Color colour)
    {
        string path = $"{Prefabs}/Traps/Models/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
        m.SetFloat("_Smoothness", .15f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // A model's objects copied out of an FBX as plain objects, each with the given material.
    static List<GameObject> FromFbx(string fbx, Transform parent, Material material)
    {
        var list = new List<GameObject>();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            var go = new GameObject(mf.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = mf.transform.localPosition; go.transform.localRotation = mf.transform.localRotation; go.transform.localScale = mf.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            list.Add(go);
        }
        return list;
    }

    static AudioClip Clip(string relative) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Library}/{relative}");

    // ── Traps ────────────────────────────────────────────────────────

    static GameObject SpikeTrap()
    {
        string path = $"{Prefabs}/Traps/Spike trap.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject("Spike trap");
        var parts = FromFbx($"{Prefabs}/Traps/Models/Spike trap.fbx", root.transform, Flat("Trap iron", new Color(.23f, .22f, .21f)));
        GameObject spikes = null;
        foreach (var p in parts) if (p.name == "Spikes") { spikes = p; p.GetComponent<MeshRenderer>().sharedMaterial = Flat("Trap rust", new Color(.36f, .2f, .12f)); }
        var trap = root.AddComponent<DungeonSpikeTrap>();
        trap.spikes = spikes != null ? spikes.transform : null;
        trap.clickAudio = Clip("SoundFX/126041__mhtaylor67__gate-latch.wav");
        trap.thrustAudio = Clip("Weapons/Wpn_Impact_Blade_Metal_01.wav");
        trap.retractAudio = Clip("Doors/Drs_Locked_01.wav");
        return Save(root, path);
    }

    static GameObject AmbushGate()
    {
        string path = $"{Prefabs}/Traps/Ambush gate.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject("Ambush gate");
        var bars = new GameObject("Bars").transform; bars.SetParent(root.transform, false);
        FromFbx($"{Prefabs}/Traps/Models/Ambush gate.fbx", bars, Flat("Gate iron", new Color(.2f, .2f, .21f)));
        var box = bars.gameObject.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 1.45f, 0f); box.size = new Vector3(2.15f, 2.9f, .16f);
        var obstacle = bars.gameObject.AddComponent<NavMeshObstacle>(); obstacle.carving = true; obstacle.center = box.center; obstacle.size = new Vector3(2.15f, 2.9f, .4f);
        return Save(root, path);
    }

    // ── Furnishing ───────────────────────────────────────────────────

    // A Synty prop copied in as plain objects with mesh colliders (painted later), under a root at the floor.
    static GameObject Furnishing(string name, string synty, float scale, Vector3? offset = null)
    {
        string path = $"{Prefabs}/Furnishing/{name}.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject(name);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Synty}/{synty}.prefab");
        var copy = (GameObject)Object.Instantiate(source, root.transform);
        copy.name = synty; copy.transform.localPosition = offset ?? Vector3.zero; copy.transform.localScale = Vector3.one * scale;
        Collide(copy);
        return Save(root, path);
    }

    // Mesh colliders on the drawn meshes (Synty's own collision meshes can't be read when the NavMesh is built in a build).
    static void Collide(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<MeshCollider>(true)) Object.DestroyImmediate(c);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.GetComponent<MeshRenderer>() == null) { Object.DestroyImmediate(mf.gameObject); continue; }
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
    }

    static GameObject CandleRack()
    {
        string path = $"{Prefabs}/Furnishing/Candle rack.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject("Candle rack");
        foreach (var n in new[] { "SM_Prop_Candle_Rack_01", "SM_Prop_Candle_Rack_01_Insert_01" })
        {
            var copy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Synty}/{n}.prefab"), root.transform);
            copy.name = n; Collide(copy);
        }
        // Flames along the candles, and their light.
        var flame = Flame();
        if (flame != null)
            for (int i = 0; i < 7; i++)
            {
                var f = Object.Instantiate(flame, root.transform);
                f.name = "Flame"; f.transform.localPosition = new Vector3(-.75f + i * .25f, 1.2f + (i % 2) * .06f, (i % 3 - 1) * .12f);
                f.transform.localScale = Vector3.one * .8f;
            }
        Light(root.transform, "Candle light", new Vector3(0f, 1.5f, -.3f), new Color(1f, .68f, .38f), 2.4f, 4.2f);
        return Save(root, path);
    }

    static GameObject HangingChain()
    {
        string path = $"{Prefabs}/Furnishing/Hanging chain.prefab";
        if (Skip(path, out var existing)) return existing;
        // Hangs down from its pivot (put it under the ceiling).
        var root = new GameObject("Hanging chain");
        var copy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Synty}/SM_Prop_Chain_02.prefab"), root.transform);
        copy.name = "SM_Prop_Chain_02"; copy.transform.localPosition = new Vector3(0f, -3.06f, 0f);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        return Save(root, path);
    }

    // The flame particle the candle props use.
    static GameObject Flame()
    {
        var group = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Groups/Barrel with candles.prefab");
        if (group == null) return null;
        foreach (var ps in group.GetComponentsInChildren<ParticleSystem>(true)) return ps.gameObject;
        return null;
    }

    static Light Light(Transform parent, string name, Vector3 at, Color colour, float intensity, float range)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = at;
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = colour; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }

    // A few painted candles on the floor, lit (candlelit rooms).
    static GameObject CandleCluster()
    {
        string path = $"{Prefabs}/Furnishing/Candle cluster.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject("Candle cluster");
        var group = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Groups/Barrel with candles.prefab");
        var tall = group.transform.Find("Breakable barrel/SM_Prop_Candle_05").gameObject;
        var short_ = group.transform.Find("Breakable barrel/SM_Prop_Candle_03").gameObject;
        var spots = new (GameObject, Vector3, float)[] { (tall, new Vector3(-.12f, 0f, .05f), 1f), (short_, new Vector3(.16f, 0f, -.04f), 1f), (short_, new Vector3(.02f, 0f, -.2f), .8f), (tall, new Vector3(.22f, 0f, .2f), .75f) };
        foreach (var (src, at, s) in spots)
        {
            var c = Object.Instantiate(src, root.transform);
            c.name = src.name; c.transform.localPosition = at; c.transform.localScale = Vector3.one * s;
        }
        Light(root.transform, "Candle light", new Vector3(0f, .6f, 0f), new Color(1f, .66f, .36f), 1.4f, 3.2f);
        return Save(root, path);
    }

    // ── The shrine ───────────────────────────────────────────────────

    static GameObject Shrine()
    {
        string path = $"{Prefabs}/Features/Shrine.prefab";
        if (Skip(path, out var existing)) return existing;
        var altar = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Features/Altar.prefab");
        var root = (GameObject)Object.Instantiate(altar);
        root.name = "Shrine";
        Object.DestroyImmediate(root.GetComponent<DungeonAltar>());
        var glow = root.transform.Find("Blessing glow"); if (glow != null) Object.DestroyImmediate(glow.gameObject);
        var skull = root.transform.Find("SM_Prop_Skull_01"); if (skull != null) Object.DestroyImmediate(skull.gameObject);
        // A heap of skulls on the table, a cluster of candles at its foot, a low red light.
        var pile = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Furnishing/Skull pile.prefab");
        if (pile != null) { var p = (GameObject)PrefabUtility.InstantiatePrefab(pile, root.transform); p.transform.localPosition = new Vector3(0f, .69f, 0f); p.transform.localScale = Vector3.one * .62f; foreach (var c in p.GetComponentsInChildren<Collider>()) c.enabled = false; }
        var cluster = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Furnishing/Candle cluster.prefab");
        var lit = new List<GameObject>();
        if (cluster != null) { var c = (GameObject)PrefabUtility.InstantiatePrefab(cluster, root.transform); c.transform.localPosition = new Vector3(-.75f, 0f, .55f); lit.Add(c.GetComponentInChildren<Light>().gameObject); }
        var red = Light(root.transform, "Shrine glow", new Vector3(0f, 1.3f, .4f), new Color(1f, .28f, .2f), 2.2f, 4f);
        lit.Add(red.gameObject);
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) lit.Add(ps.gameObject);
        var shrine = root.AddComponent<DungeonShrine>();
        shrine.litWhileUnused = lit.ToArray();
        shrine.offerClip = Clip("Blood Guts Bones/Blood_Squirts_02.wav");
        return Save(root, path);
    }

    // ── Moonlight ────────────────────────────────────────────────────

    // A cold spot light from high above, a shaft of pale dust down to the floor and slow motes in it. Put it at the
    // room's centre on the floor; the skylight above it is the room template's (Open Ceiling).
    static GameObject Moonlight()
    {
        string path = $"{Prefabs}/Props/Effects/Moonlight.prefab";
        if (Skip(path, out var existing)) return existing;
        var root = new GameObject("Moonlight");
        var spot = new GameObject("Moon").transform; spot.SetParent(root.transform, false);
        spot.localPosition = new Vector3(-1.2f, 11f, -.6f); spot.localRotation = Quaternion.LookRotation(new Vector3(1.2f, -11f, .6f));
        var l = spot.gameObject.AddComponent<Light>(); l.type = LightType.Spot; l.color = new Color(.62f, .74f, 1f);
        l.intensity = 150f; l.range = 18f; l.spotAngle = 36f; l.innerSpotAngle = 22f; l.shadows = LightShadows.Hard;
        // Above the skylight: a patch of night sky, so the opening doesn't look out on the room the table stands in.
        var sky = GameObject.CreatePrimitive(PrimitiveType.Quad); sky.name = "Night sky";
        Object.DestroyImmediate(sky.GetComponent<Collider>());
        sky.transform.SetParent(root.transform, false); sky.transform.localPosition = new Vector3(0f, 7.6f, 0f);
        sky.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); sky.transform.localScale = new Vector3(16f, 16f, 1f);
        var skyRenderer = sky.GetComponent<MeshRenderer>(); skyRenderer.sharedMaterial = SkyMaterial(); skyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var shaft = FromFbx($"{Prefabs}/Traps/Models/Moon shaft.fbx", root.transform, ShaftMaterial());
        foreach (var s in shaft) { s.name = "Shaft"; s.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        // Motes drifting in the light.
        var motes = new GameObject("Motes"); motes.transform.SetParent(root.transform, false); motes.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var ps = motes.AddComponent<ParticleSystem>();
        var main = ps.main; main.startLifetime = 9f; main.startSpeed = .04f; main.startSize = .035f; main.maxParticles = 70; main.startColor = new Color(.75f, .85f, 1f, .5f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 7f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.8f, 3f, 1.8f);
        var noise = ps.noise; noise.enabled = true; noise.strength = .05f; noise.frequency = .2f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = motes.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = ShaftMaterial(); pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return Save(root, path);
    }

    // Additive like the candle flames (a copy of their material, no texture), faint pale blue; the shaft's vertex
    // colours fade it out at both ends.
    static Material ShaftMaterial()
    {
        string path = $"{Prefabs}/Traps/Models/Moon shaft.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null && !overwrite) return m;
        var flame = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonDarkFantasy/Materials/FX/Additive_Circle_Glow_01 1.mat");
        var made = flame != null ? new Material(flame) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        made.SetTexture("_BaseMap", null);
        // Multiply colour mode (the flames add theirs, which turns a plain shaft white).
        made.DisableKeyword("_COLORADDSUBDIFF_ON"); if (made.HasProperty("_ColorMode")) made.SetFloat("_ColorMode", 0f);
        made.SetColor("_BaseColor", new Color(.16f, .21f, .34f, .5f));
        if (m != null) { m.shader = made.shader; m.CopyPropertiesFromMaterial(made); m.shaderKeywords = made.shaderKeywords; m.renderQueue = made.renderQueue; EditorUtility.SetDirty(m); return m; }
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    static Material SkyMaterial()
    {
        string path = $"{Prefabs}/Traps/Models/Night sky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        string tex = $"{Prefabs}/Traps/Models/Night sky.png";
        if (AssetImporter.GetAtPath(tex) is TextureImporter ti) { ti.filterMode = FilterMode.Point; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex)); m.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ── Wiring ───────────────────────────────────────────────────────

    static void Wire()
    {
        var shared = AssetDatabase.LoadAssetAtPath<TableLevelDefaults>("Assets/Game/Levels/Resources/Table level defaults.asset");
        if (shared == null) return;
        T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(p);
        if (shared.shrinePrefab == null) shared.shrinePrefab = Load<GameObject>($"{Prefabs}/Features/Shrine.prefab");
        if (shared.spikeTrapPrefab == null) shared.spikeTrapPrefab = Load<GameObject>($"{Prefabs}/Traps/Spike trap.prefab");
        if (shared.ambushGatePrefab == null) shared.ambushGatePrefab = Load<GameObject>($"{Prefabs}/Traps/Ambush gate.prefab");
        if (shared.ambushSpawnPuff == null) shared.ambushSpawnPuff = Load<GameObject>($"{Prefabs}/Props/Effects/Dust puff.prefab");
        if (shared.ambushSlamAudio == null) shared.ambushSlamAudio = Clip("Doors/Drs_Dungeon_MetalDoubleDoor_Close_01.wav");
        if (shared.ambushLiftAudio == null) shared.ambushLiftAudio = Clip("Doors/Drs_Dungeon_MetalDoubleDoor_Open_01.wav");
        if (shared.ambushSpawnAudio == null) shared.ambushSpawnAudio = Clip("SoundFX/Poof.wav");
        if (shared.sleepAudio == null) shared.sleepAudio = Clip("SoundFX/snoring1.wav");
        EditorUtility.SetDirty(shared);
    }
}
