// The frozen look of an enemy hit by a Frost Lance (FrostCoat, the user, 2026-11-13): makes the bake material, the breath
// and flake materials (from Tools/frost_particles.py's sprites), and puts FrostCoat with its Breath and Rime flakes particle
// systems on Combat/Spells/Frost Lance/Frost Lance Chill.prefab (outside its Effect child, which SpellPackEffects rebuilds).
// Safe to run again: it replaces its own pieces only.
const string Dir = "Assets/Game/Combat/Spells/Frost Lance/Frost";
const string Chill = "Assets/Game/Combat/Spells/Frost Lance/Frost Lance Chill.prefab";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

foreach (var name in new[] { "Frost breath", "Rime flake" })
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

var bake = Mat(Dir + "/Frost bake.mat", Shader.Find("Hidden/LoomRoom/Frost Bake"));
UnityEditor.EditorUtility.SetDirty(bake);

// Lit, alpha-blended particles: the puffs and flakes take the room's light like the body does, never a glow in the dark.
Material Particles(string name)
{
    var m = Mat(Dir + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
    var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + name + ".png");
    m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
    m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_Cull", 2); m.SetFloat("_ZWrite", 0);
    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_ReceiveShadows", 0); m.SetFloat("_SpecularHighlights", 0); m.SetFloat("_ColorMode", 0);
    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
    m.SetOverrideTag("RenderType", "Transparent");
    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    UnityEditor.EditorUtility.SetDirty(m);
    return m;
}
var breathMat = Particles("Frost breath");
var flakeMat = Particles("Rime flake");

// Alpha in hard steps over a particle's life (pixel art: no smooth fade).
Gradient Steps(Color c, params (float t, float a)[] keys)
{
    var g = new Gradient { mode = GradientMode.Fixed };
    var alphas = new GradientAlphaKey[keys.Length];
    for (int i = 0; i < keys.Length; i++) alphas[i] = new GradientAlphaKey(keys[i].a, keys[i].t);
    g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, alphas);
    return g;
}

ParticleSystem System(GameObject root, string name, Material mat, int tiles)
{
    var old = root.transform.Find(name);
    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var go = new GameObject(name);
    go.transform.SetParent(root.transform, false);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    var r = go.GetComponent<ParticleSystemRenderer>();
    r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = -10;
    var sheet = ps.textureSheetAnimation;
    sheet.enabled = true; sheet.numTilesX = tiles; sheet.numTilesY = 1;
    sheet.animation = ParticleSystemAnimationType.WholeSheet;
    sheet.startFrame = new ParticleSystem.MinMaxCurve(0, (tiles - .01f) / tiles);   // a random tile each, held
    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0);
    sheet.cycleCount = 1;
    return ps;
}

var prefab = UnityEditor.PrefabUtility.LoadPrefabContents(Chill);
try
{
    // The cold breath: puffs blown out at the head by FrostCoat, growing and fading in steps as they drift up.
    var breath = System(prefab, "Breath", breathMat, 4);
    {
        var main = breath.main;
        main.duration = 1; main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 40;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .45f);
        main.startSize = new ParticleSystem.MinMaxCurve(.07f, .11f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, 0);
        main.startColor = new Color(.78f, .86f, .95f, .55f);
        main.gravityModifier = -.02f;
        var em = breath.emission; em.enabled = true; em.rateOverTime = 0;
        var shape = breath.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 16; shape.radius = .02f;
        var size = breath.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 2.4f));
        var colour = breath.colorOverLifetime; colour.enabled = true;
        colour.color = Steps(Color.white, (0, .5f), (.2f, 1), (.55f, .62f), (.8f, .3f), (1, 0));
        var vel = breath.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(.05f, .12f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var limit = breath.limitVelocityOverLifetime; limit.enabled = true; limit.limit = .12f; limit.dampen = .08f;
    }

    // Rime flakes drifting down off the body.
    var flakes = System(prefab, "Rime flakes", flakeMat, 4);
    {
        var main = flakes.main;
        main.duration = 1; main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 30;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.3f);
        main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.035f, .05f);
        main.startColor = new Color(.85f, .93f, 1f, 1f);
        main.gravityModifier = .06f;
        var em = flakes.emission; em.enabled = true; em.rateOverTime = 9;
        var shape = flakes.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(.5f, 1.4f, .4f);
        var colour = flakes.colorOverLifetime; colour.enabled = true;
        colour.color = Steps(Color.white, (0, 1), (.65f, .62f), (.85f, .3f), (1, 0));
        var noise = flakes.noise; noise.enabled = true; noise.strength = .15f; noise.frequency = .8f; noise.scrollSpeed = .3f;
    }

    // The pack effect's glow, sphere and ice swirls go (SpellPackEffects drops them too), and its light (NewSpellsSetup no
    // longer adds it): inside the body the light lit its middle white, and the swirls were smooth streaks.
    foreach (var light in prefab.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light.gameObject == prefab ? (UnityEngine.Object)light : light.gameObject);
    var visual = prefab.GetComponent<SpellVisual>(); if (visual != null) visual.glow = null;
    // Then its snow and its root's own emission too: smooth blue streaks on the chest of a big body (the Crypt Lord).
    foreach (var name in new[] { "Glow", "Sphere", "Ice", "Snow" })
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            if (t.name == name && t.parent != null && t.parent.name == "Effect") { UnityEngine.Object.DestroyImmediate(t.gameObject); break; }
    var packRoot = prefab.transform.Find("Effect");
    if (packRoot != null && packRoot.TryGetComponent<ParticleSystem>(out var rootSystem)) { var e = rootSystem.emission; e.enabled = false; }

    var coat = prefab.GetComponent<FrostCoat>();
    if (coat == null) coat = prefab.AddComponent<FrostCoat>();
    coat.bake = bake; coat.breath = breath; coat.flakes = flakes;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, Chill);
    sb.Append("chill prefab: FrostCoat, Breath, Rime flakes\n");
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); }

// A Frost Lance hit slows by half for 3 seconds (the user, 2026-11-13; it was 40%).
var lance = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>("Assets/Game/Combat/Spells/Frost Lance/Frost Lance.asset");
var so = new UnityEditor.SerializedObject(lance);
so.FindProperty("chill").floatValue = .5f; so.FindProperty("chillSeconds").floatValue = 3f;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.AssetDatabase.SaveAssets();
sb.Append($"Frost Lance chill {lance.chill} for {lance.chillSeconds} s\n");
return sb.ToString();
