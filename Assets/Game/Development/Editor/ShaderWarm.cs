using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Why this exists: the game froze for about a second at random moments in the editor (a chest opening, the Dungeon
// Master's hand reaching in, a floor landing on the table). Each freeze was the editor compiling one shader variant the
// first time something was drawn with it. A variant is a shader with one exact set of keywords: the material's own (normal
// map, alpha test, emission) plus the ones the lighting of the moment switches on (soft shadows, shadow cascades, fog,
// light cookies, shadowed lamps). The project compiles them blocking (Editor settings, Asynchronous Shader Compilation
// off since 2026-11-04, so no cyan stand-ins show), and every new lighting state (soft shadows off on dungeon floors
// since 2026-11-10, a foggy biome) makes every material need a variant nobody has compiled yet: one freeze each, once
// per computer. A build has them all compiled and does not freeze.
//
// What it does: it keeps a list of every variant the editor has drawn (its own tracking, read when Play stops; kept per
// computer in Library/ShaderWarm, plus the shared list Tools/shader_warm_seed.tsv from the repository), and for each
// shader it compiles every material keyword set it has seen with every lighting keyword set seen on any shader (each
// cut to the lighting keywords that shader has), in the background on all cores, while the game is not being played.
// So a material seen once in any light is ready for every light the game has, and nothing is left to compile in the
// middle of Play. Nothing in the game changes. (Until 2026-11-11 the lighting sets were a shader's own only: the new
// fireball's lava rock, a particle shader seen only on dungeon floors, froze the intro for two seconds when the intro's
// killer first threw one in the intro's light, which only the ordinary lit shaders had been seen in.)
//
// How: ShaderUtil.CompilePass on a throwaway material, with the lighting keywords switched on globally for the call and
// put back after. Compiling in the background needs the editor's async setting, so it is switched on only inside the
// call that queues the work and switched off again before the call returns: the setting on disk never changes and
// nothing is ever drawn while it is on. Compiled variants land in Library/ShaderCache like any other.
//
// From an eval: ShaderWarm.Status() says where it is, ShaderWarm.Run() reads the editor's list and starts a pass now,
// ShaderWarm.SaveSeed() adds this computer's list to the shared one (commit it so other computers warm the same
// variants before they are first seen there). EditorPrefs "LoomRoom.ShaderWarm.Off" = true switches it off here.
[InitializeOnLoad]
public static class ShaderWarm
{
    // ── Settings ──
    const string OffKey = "LoomRoom.ShaderWarm.Off";
    const string LedgerFolder = "Library/ShaderWarm";
    const string LedgerPath = LedgerFolder + "/ledger.tsv";
    const string SeedPath = "Tools/shader_warm_seed.tsv";
    const string TempAsset = "Assets/_ShaderWarmTemp.shadervariants";
    const int BatchSize = 32;             // variants queued per editor tick; the next batch waits until these are compiled
    const int TickMilliseconds = 6;       // most time one tick may spend checking what is compiled
    const int MaxCellsPerShader = 6000;   // a shader whose grid is larger than this is left to compile as it is seen
    const double StartDelay = 8;          // seconds after a script reload before the first pass

    // Passes the game never draws: compiling them would only fill the cache.
    static readonly HashSet<string> SkippedLightModes = new HashSet<string>
        { "Meta", "Universal2D", "UniversalGBuffer", "SceneSelectionPass", "Picking", "XRMotionVectors", "MotionVectors" };

    // ── State ──
    sealed class Group
    {
        public Shader shader;
        public int passType;
        public readonly HashSet<string> locals = new HashSet<string>();
        public readonly HashSet<string> globals = new HashSet<string>();
    }

    struct Job
    {
        public Shader shader;
        public string[] locals, globals;
        public int[] passes;
    }

    static readonly HashSet<string> ledger = new HashSet<string>();
    static readonly Dictionary<Shader, Material> materials = new Dictionary<Shader, Material>();
    static readonly Dictionary<Shader, LocalKeyword[]> overridable = new Dictionary<Shader, LocalKeyword[]>();
    static readonly Dictionary<string, GlobalKeyword> globalKeywords = new Dictionary<string, GlobalKeyword>();
    static readonly Dictionary<string, bool> touched = new Dictionary<string, bool>();
    static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");

    static List<Job> jobs;
    static int next, asked, cells, seedRows, skippedShaders;
    static bool harvestDue, walkDue, loaded;
    static double notBefore;
    static string lastPass = "none yet";

    static readonly MethodInfo SaveTracked = typeof(ShaderUtil).GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    static readonly MethodInfo ClearTracked = typeof(ShaderUtil).GetMethod("ClearCurrentShaderVariantCollection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    static readonly MethodInfo TrackedCount =typeof(ShaderUtil).GetMethod("GetCurrentShaderVariantCollectionVariantCount", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    static ShaderWarm()
    {
        if (Application.isBatchMode) return;
        notBefore = EditorApplication.timeSinceStartup + StartDelay;
        walkDue = true;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            harvestDue = true;
            walkDue = true;
            notBefore = EditorApplication.timeSinceStartup + 1;
        };
    }

    // ── From an eval ──
    public static string Status()
    {
        Load();
        string walk = jobs == null ? "idle" : "pass running, " + next + " of " + jobs.Count + " cells checked";
        return "ledger " + ledger.Count + " variants (" + seedRows + " from the shared list), " + walk + ", " + asked + " variants asked for in the running or last pass"
            + (ShaderUtil.anythingCompiling ? ", compiling now" : "") + "; last finished pass: " + lastPass
            + (EditorPrefs.GetBool(OffKey, false) ? "; SWITCHED OFF on this computer" : "");
    }

    public static string Run()
    {
        loaded = false;      // read both lists again: the shared one may have come in with a pull
        ledger.Clear();
        harvestDue = true;
        walkDue = true;
        notBefore = 0;
        return "started; " + Status();
    }

    public static string SaveSeed()
    {
        Load();
        Harvest();
        var all = new SortedSet<string>(ledger, StringComparer.Ordinal);
        int before = File.Exists(SeedPath) ? File.ReadAllLines(SeedPath).Length : 0;
        File.WriteAllText(SeedPath, string.Join("\n", all) + "\n");
        seedRows = all.Count;
        return SeedPath + ": " + before + " to " + all.Count + " variants";
    }

    // ── The tick ──
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < notBefore || Busy()) return;
        if (EditorPrefs.GetBool(OffKey, false)) { harvestDue = walkDue = false; jobs = null; return; }

        if (harvestDue)
        {
            harvestDue = false;
            Load();
            Harvest();
        }
        if (walkDue && jobs == null)
        {
            walkDue = false;
            Load();
            BuildJobs();
        }
        if (jobs == null || ShaderUtil.anythingCompiling) return;
        Step();
        if (next >= jobs.Count) Finish();
    }

    static bool Busy() => EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer;

    // One slice of the pass: find the next cells whose variants are not compiled yet, then queue those in the background.
    static void Step()
    {
        var watch = Stopwatch.StartNew();
        var needed = new List<(int job, int pass)>();
        touched.Clear();
        try
        {
            while (next < jobs.Count && needed.Count < BatchSize && watch.ElapsedMilliseconds < TickMilliseconds)
            {
                var job = jobs[next];
                if (job.shader != null)
                {
                    var material = Prepare(job);
                    foreach (int pass in job.passes)
                        if (!ShaderUtil.IsPassCompiled(material, pass)) needed.Add((next, pass));
                }
                next++;
            }
            if (needed.Count == 0) return;

            bool wasSetting = EditorSettings.asyncShaderCompilation, wasAllowed = ShaderUtil.allowAsyncCompilation;
            if (!wasSetting) EditorSettings.asyncShaderCompilation = true;
            ShaderUtil.allowAsyncCompilation = true;
            try
            {
                foreach (var (index, pass) in needed)
                {
                    var material = Prepare(jobs[index]);
                    ShaderUtil.CompilePass(material, pass, false);
                    asked++;
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = wasAllowed;
                if (!wasSetting) EditorSettings.asyncShaderCompilation = false;
            }
        }
        finally
        {
            foreach (var pair in touched) Shader.SetKeyword(Global(pair.Key), pair.Value);
            touched.Clear();
        }
    }

    // The throwaway material with the job's own keywords, and the job's lighting keywords switched on for the moment.
    static Material Prepare(Job job)
    {
        if (!materials.TryGetValue(job.shader, out var material) || material == null)
            materials[job.shader] = material = new Material(job.shader) { hideFlags = HideFlags.HideAndDontSave };
        material.shaderKeywords = job.locals;

        foreach (var keyword in Overridable(job.shader))
        {
            bool want = Array.IndexOf(job.globals, keyword.name) >= 0;
            var global = Global(keyword.name);
            bool on = Shader.IsKeywordEnabled(global);
            if (on == want) continue;
            if (!touched.ContainsKey(keyword.name)) touched[keyword.name] = on;
            Shader.SetKeyword(global, want);
        }
        return material;
    }

    static LocalKeyword[] Overridable(Shader shader)
    {
        if (overridable.TryGetValue(shader, out var list)) return list;
        var found = new List<LocalKeyword>();
        foreach (var keyword in shader.keywordSpace.keywords)
            if (keyword.isOverridable) found.Add(keyword);
        return overridable[shader] = found.ToArray();
    }

    static GlobalKeyword Global(string name)
    {
        if (!globalKeywords.TryGetValue(name, out var keyword)) globalKeywords[name] = keyword = GlobalKeyword.Create(name);
        return keyword;
    }

    static void Finish()
    {
        lastPass = DateTime.Now.ToString("HH:mm") + ", " + cells + " cells, " + asked + " variants asked for"
            + (skippedShaders > 0 ? ", " + skippedShaders + " shaders with too large a grid left out" : "");
        jobs = null;
        foreach (var material in materials.Values)
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        materials.Clear();
    }

    // ── The list of variants ──
    static void Load()
    {
        if (loaded) return;
        loaded = true;
        seedRows = 0;
        if (File.Exists(SeedPath))
            foreach (var line in File.ReadAllLines(SeedPath))
                if (line.Length > 0 && ledger.Add(line)) seedRows++;
        if (File.Exists(LedgerPath))
            foreach (var line in File.ReadAllLines(LedgerPath))
                if (line.Length > 0) ledger.Add(line);
    }

    // Reads the variants the editor has drawn since its last script reload. Unity only writes that list as an asset, so it
    // goes to a file in Assets for a moment and is deleted again.
    static void Harvest()
    {
        if (SaveTracked == null || TrackedCount == null) return;
        // The editor lists a variant when its program is first made in this editor run (so a Play that needed nothing
        // new lists nothing). The list is emptied once read, so the next read holds only what came after.
        if ((int)TrackedCount.Invoke(null, null) == 0) return;

        int added = 0;
        try
        {
            SaveTracked.Invoke(null, new object[] { TempAsset });
            AssetDatabase.ImportAsset(TempAsset);
            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(TempAsset);
            if (collection == null) return;
            using (var serialized = new SerializedObject(collection))
            {
                var shaders = serialized.FindProperty("m_Shaders");
                for (int i = 0; i < shaders.arraySize; i++)
                {
                    var entry = shaders.GetArrayElementAtIndex(i);
                    var shader = entry.FindPropertyRelative("first").objectReferenceValue as Shader;
                    if (shader == null) continue;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long _);
                    var variants = entry.FindPropertyRelative("second.variants");
                    for (int v = 0; v < variants.arraySize; v++)
                    {
                        var variant = variants.GetArrayElementAtIndex(v);
                        var words = variant.FindPropertyRelative("keywords").stringValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        Array.Sort(words, StringComparer.Ordinal);
                        string line = guid + "\t" + shader.name + "\t" + variant.FindPropertyRelative("passType").intValue + "\t" + string.Join(" ", words);
                        if (ledger.Add(line)) added++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[ShaderWarm] Could not read the editor's list of drawn shader variants: " + e.Message);
        }
        finally
        {
            if (File.Exists(TempAsset)) AssetDatabase.DeleteAsset(TempAsset);
            ClearTracked?.Invoke(null, null);
        }

        if (added == 0) return;
        Directory.CreateDirectory(LedgerFolder);
        File.WriteAllText(LedgerPath, string.Join("\n", new SortedSet<string>(ledger, StringComparer.Ordinal)) + "\n");
    }

    // Per shader and kind of pass: every material keyword set seen, times every lighting keyword set seen.
    static void BuildJobs()
    {
        var groups = new Dictionary<string, Group>();
        var shaders = new Dictionary<string, Shader>();
        foreach (var line in ledger)
        {
            var parts = line.Split('\t');
            if (parts.Length < 4 || !int.TryParse(parts[2], out int passType)) continue;
            string shaderKey = parts[0] + "\t" + parts[1];
            if (!shaders.TryGetValue(shaderKey, out var shader)) shaders[shaderKey] = shader = Find(parts[0], parts[1]);
            if (shader == null) continue;

            string key = shaderKey + "\t" + passType;
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group { shader = shader, passType = passType };
            var space = shader.keywordSpace;
            var locals = new List<string>();
            var globals = new List<string>();
            foreach (var word in parts[3].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var keyword = space.FindKeyword(word);
                if (keyword.isValid && !keyword.isOverridable) locals.Add(word); else globals.Add(word);
            }
            group.locals.Add(string.Join(" ", locals));
            group.globals.Add(string.Join(" ", globals));
        }

        // Every lighting set seen, per kind of pass: each shader borrows the sets seen on the others, cut to the lighting
        // keywords it has itself (a set it has nothing of becomes the empty set, which every shader can be drawn with).
        var pooled = new Dictionary<int, HashSet<string>>();
        foreach (var group in groups.Values)
        {
            if (!pooled.TryGetValue(group.passType, out var sets)) pooled[group.passType] = sets = new HashSet<string>();
            sets.UnionWith(group.globals);
        }
        foreach (var group in groups.Values)
        {
            var space = group.shader.keywordSpace;
            foreach (var set in pooled[group.passType])
            {
                var kept = new List<string>();
                foreach (var word in set.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var keyword = space.FindKeyword(word);
                    if (keyword.isValid && keyword.isOverridable) kept.Add(word);
                }
                group.globals.Add(string.Join(" ", kept));
            }
        }

        jobs = new List<Job>();
        next = asked = cells = skippedShaders = 0;
        foreach (var group in groups.Values)
        {
            var passes = new List<int>();
            for (int pass = 0; pass < group.shader.passCount; pass++)
                if (TypeOf(group.shader, pass) == group.passType) passes.Add(pass);
            if (passes.Count == 0) continue;
            if ((long)group.locals.Count * group.globals.Count > MaxCellsPerShader) { skippedShaders++; continue; }

            var passArray = passes.ToArray();
            foreach (var locals in group.locals)
            {
                var localArray = locals.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var globals in group.globals)
                    jobs.Add(new Job { shader = group.shader, locals = localArray, globals = globals.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), passes = passArray });
            }
        }
        cells = jobs.Count;
    }

    static Shader Find(string guid, string name)
    {
        string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path) && (path.StartsWith("Assets/") || path.StartsWith("Packages/")))
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Shader shader && shader.name == name) return shader;
        return Shader.Find(name);
    }

    // The kind of pass as the editor's list names it (PassType), from the pass's LightMode tag; -1 for a pass the game never draws.
    static int TypeOf(Shader shader, int pass)
    {
        string mode = shader.FindPassTagValue(pass, LightMode).name;
        if (string.IsNullOrEmpty(mode) || mode == "Always") return (int)PassType.Normal;
        if (SkippedLightModes.Contains(mode)) return -1;
        switch (mode)
        {
            case "ShadowCaster": return (int)PassType.ShadowCaster;
            case "SRPDefaultUnlit": return (int)PassType.ScriptableRenderPipelineDefaultUnlit;
            case "Vertex": return (int)PassType.Vertex;
            case "VertexLM": return (int)PassType.VertexLM;
            case "ForwardBase": return (int)PassType.ForwardBase;
            case "ForwardAdd": return (int)PassType.ForwardAdd;
            case "Deferred": return (int)PassType.Deferred;
            default: return (int)PassType.ScriptableRenderPipeline;
        }
    }
}
