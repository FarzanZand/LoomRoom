// The burning look of an enemy set alight by a Fireball (BurnCoat, the user, 2026-11-13): the bake material, the flame,
// ember and smoke materials (from Tools/burn_particles.py's sprites; the smoke uses the frost breath's puffs), and the
// effect Combat/Spells/Fireball/Fireball Burn.prefab (SpellVisual, BurnCoat, Flames, Sparks, Smoke, Fire light), set as the
// Fireball's burn: half its hit in all over 5 seconds. Safe to run again: it replaces its own pieces only.
const string Dir = "Assets/Game/Combat/Spells/Fireball/Burn";
const string Path = "Assets/Game/Combat/Spells/Fireball/Fireball Burn.prefab";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

foreach (var name in new[] { "Flame", "Ember" })
{
    var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(Dir + "/" + name + ".png");
    ti.textureType = UnityEditor.TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
    ti.filterMode = FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
}

Material Mat(string path, Shader shader)
{
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { m = new Material(shader); UnityEditor.AssetDatabase.CreateAsset(m, path); }
    m.shader = shader;
    return m;
}
var bake = Mat(Dir + "/Burn bake.mat", Shader.Find("Hidden/LoomRoom/Burn Bake"));
UnityEditor.EditorUtility.SetDirty(bake);

// Alpha-blended particles, unlit: fire is the light, and the smoke is dark (it stays dark in the firelight).
Material Particles(string name, Texture2D tex, bool lit)
{
    var m = Mat(Dir + "/" + name + ".mat", Shader.Find(lit ? "Universal Render Pipeline/Particles/Simple Lit" : "Universal Render Pipeline/Particles/Unlit"));
    m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
    m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_Cull", 2); m.SetFloat("_ZWrite", 0);
    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_ColorMode", 0);
    if (lit) { m.SetFloat("_ReceiveShadows", 0); m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_RECEIVE_SHADOWS_OFF"); }
    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    m.SetOverrideTag("RenderType", "Transparent");
    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    UnityEditor.EditorUtility.SetDirty(m);
    return m;
}
var flameMat = Particles("Flame", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Flame.png"), false);
var emberMat = Particles("Ember", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Ember.png"), false);
var smokeMat = Particles("Burn smoke", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Combat/Spells/Frost Lance/Frost/Frost breath.png"), false);
// The puffs are drawn pale; the smoke's darkness is on its material, and it is unlit (lit, the firelight just under it
// turned it white; dark, it never glows).
smokeMat.SetColor("_BaseColor", new Color(.16f, .14f, .13f, 1f)); UnityEditor.EditorUtility.SetDirty(smokeMat);

Gradient Steps(Color c, params (float t, float a)[] keys)
{
    var g = new Gradient { mode = GradientMode.Fixed };
    var alphas = new GradientAlphaKey[keys.Length];
    for (int i = 0; i < keys.Length; i++) alphas[i] = new GradientAlphaKey(keys[i].a, keys[i].t);
    g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, alphas);
    return g;
}

ParticleSystem System(GameObject root, string name, Material mat, int tiles, bool overLife, bool upright)
{
    var old = root.transform.Find(name);
    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var go = new GameObject(name);
    go.transform.SetParent(root.transform, false);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    var r = go.GetComponent<ParticleSystemRenderer>();
    r.sharedMaterial = mat; r.renderMode = upright ? ParticleSystemRenderMode.VerticalBillboard : ParticleSystemRenderMode.Billboard;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    var sheet = ps.textureSheetAnimation;
    sheet.enabled = true; sheet.numTilesX = tiles; sheet.numTilesY = 1;
    sheet.animation = ParticleSystemAnimationType.WholeSheet;
    if (overLife) { sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, .999f)); sheet.startFrame = 0; }
    else { sheet.startFrame = new ParticleSystem.MinMaxCurve(0, (tiles - .01f) / tiles); sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0); }
    sheet.cycleCount = 1;
    var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.playOnAwake = true; main.duration = 1;
    return ps;
}

bool exists = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null;
var prefab = exists ? UnityEditor.PrefabUtility.LoadPrefabContents(Path) : new GameObject("Fireball Burn");
try
{
    // Flames licking up off the body (BurnCoat points them at its skinned mesh, or a box round it).
    var flames = System(prefab, "Flames", flameMat, 4, true, true);
    {
        var main = flames.main; main.maxParticles = 70;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.32f, .5f); main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(.13f, .22f); main.startColor = Color.white;
        var em = flames.emission; em.rateOverTime = 55;
        var shape = flames.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(.5f, 1.2f, .4f);
        var vel = flames.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(.55f, .95f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }
    // Embers rising and drifting off it.
    var sparks = System(prefab, "Sparks", emberMat, 2, false, false);
    {
        var main = sparks.main; main.maxParticles = 40;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.1f); main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(.035f, .05f); main.startColor = Color.white;
        var em = sparks.emission; em.rateOverTime = 12;
        var shape = sparks.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(.4f, 1f, .3f);
        var vel = sparks.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-.15f, .15f); vel.y = new ParticleSystem.MinMaxCurve(.9f, 1.6f); vel.z = new ParticleSystem.MinMaxCurve(-.15f, .15f);
        var noise = sparks.noise; noise.enabled = true; noise.strength = .35f; noise.frequency = 1.2f;
        var colour = sparks.colorOverLifetime; colour.enabled = true; colour.color = Steps(Color.white, (0, 1), (.7f, 1), (.85f, .55f), (1, 0));
    }
    // Dark smoke puffed out over its head by BurnCoat, rising.
    var smoke = System(prefab, "Smoke", smokeMat, 4, false, false);
    {
        smoke.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var main = smoke.main; main.maxParticles = 30;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(.35f, .55f);
        main.startSize = new ParticleSystem.MinMaxCurve(.14f, .22f); main.startColor = new Color(1f, 1f, 1f, .8f);
        var em = smoke.emission; em.rateOverTime = 0;
        var shape = smoke.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 14; shape.radius = .06f;
        var size = smoke.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 2.3f));
        var colour = smoke.colorOverLifetime; colour.enabled = true; colour.color = Steps(Color.white, (0, .6f), (.2f, 1), (.6f, .62f), (.85f, .3f), (1, 0));
    }
    // The firelight, over its head (BurnCoat keeps it there).
    var old = prefab.transform.Find("Fire light"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var lightGo = new GameObject("Fire light"); lightGo.transform.SetParent(prefab.transform, false);
    var light = lightGo.AddComponent<Light>();
    light.type = LightType.Point; light.color = new Color(1f, .58f, .26f); light.range = 3.2f; light.intensity = 1.6f; light.shadows = LightShadows.None;

    var visual = prefab.GetComponent<SpellVisual>(); if (visual == null) visual = prefab.AddComponent<SpellVisual>();
    visual.glow = null; visual.stopLinger = .45f; visual.autoDestroySeconds = 0;
    var coat = prefab.GetComponent<BurnCoat>(); if (coat == null) coat = prefab.AddComponent<BurnCoat>();
    coat.embers = .08f; coat.glow = new Color(1.5f, .42f, .07f);
    coat.bake = bake; coat.flames = flames; coat.sparks = sparks; coat.smoke = smoke; coat.fireLight = light;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, Path);
    sb.Append("burn prefab made\n");
}
finally { if (exists) UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); else UnityEngine.Object.DestroyImmediate(prefab); }

// The Fireball burns for half its hit over 5 seconds (the user, 2026-11-13; 3 seconds at first).
var fireball = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>("Assets/Game/Combat/Spells/Fireball/Fireball.asset");
var so = new UnityEditor.SerializedObject(fireball);
so.FindProperty("burn").floatValue = .5f; so.FindProperty("burnSeconds").floatValue = 5f;
so.FindProperty("burnEffect").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<SpellVisual>();
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.AssetDatabase.SaveAssets();
sb.Append($"Fireball burn {fireball.burn} over {fireball.burnSeconds} s, effect {fireball.burnEffect}\n");
return sb.ToString();
