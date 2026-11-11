// The Party Crasher's confetti burst: Items/Effects/Party confetti.prefab (sheets from Tools/party_confetti.py, in
// Items/Effects/Party confetti/). Dungeon scale (1 unit = 1 m), every particle drawn at the dungeon's 128 texels a metre.
// A pop of rays and a short warm flash, then about 140 flecks of confetti in eight colours thrown out about 0.6-0.8 m, slowed by
// drag, turning over as they flutter down and fading by 2 s, with gold and white sparkles twinkling among them. Plays on
// spawn, simulates in world space (the body it came off flies on), always simulates, and destroys itself when done.
// Safe to run again: it rebuilds the prefab, materials and light from these numbers.
const string Dir = "Assets/Game/Items/Effects/Party confetti", PrefabPath = "Assets/Game/Items/Effects/Party confetti.prefab";
const string FromMat = "Assets/Game/Combat/Spells/_Pack/Materials/Glow_LWRP.mat";
const float TexelsPerMetre = 128f;
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

System.Func<string, Texture2D> texture = name =>
{
    string path = Dir + "/" + name + ".png";
    var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
    ti.textureType = UnityEditor.TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
    ti.filterMode = FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 0; ti.isReadable = false;
    ti.SaveAndReimport();
    return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
};
// A copy of the Spells Pack's glow material (URP particles, unlit, transparent) with soft particles off: fading where it
// meets a surface made flecks lying on the floor look like glass.
System.Func<string, Texture2D, bool, float, Material> material = (name, tex, additive, bright) =>
{
    string path = Dir + "/" + name + ".mat";
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path) == null) UnityEditor.AssetDatabase.CopyAsset(FromMat, path);
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex);
    m.SetColor("_BaseColor", new Color(bright, bright, bright, 1));
    m.SetFloat("_SoftParticlesEnabled", 0); m.DisableKeyword("_SOFTPARTICLES_ON");
    m.SetFloat("_Blend", additive ? 2 : 0);
    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
    m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
    m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
    UnityEditor.EditorUtility.SetDirty(m); return m;
};
System.Func<Color32[], Gradient> palette = cs =>
{
    var g = new Gradient { mode = GradientMode.Fixed };
    var ck = new GradientColorKey[cs.Length];
    for (int i = 0; i < cs.Length; i++) ck[i] = new GradientColorKey(cs[i], (i + 1f) / cs.Length);
    g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
    return g;
};
System.Func<float, Gradient> fadeAfter = from =>
{
    var g = new Gradient();
    g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
              new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, from), new GradientAlphaKey(0, 1) });
    return g;
};
System.Action<ParticleSystem> common = ps =>
{
    // A system added in the editor starts playing at once, and its duration can't be set while it plays.
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    var main = ps.main;
    main.playOnAwake = true; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
    main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
    var em = ps.emission; em.enabled = true; em.rateOverTime = 0;
    var r = ps.GetComponent<ParticleSystemRenderer>();
    r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    r.minParticleSize = 0; r.maxParticleSize = 5;
};

var confettiTex = texture("Confetti"); var sparkleTex = texture("Sparkle"); var popTex = texture("Pop");
var confettiMat = material("Confetti", confettiTex, false, 1f);
var sparkleMat = material("Sparkle", sparkleTex, true, 1.5f);
var popMat = material("Pop", popTex, true, 1.3f);

// ── The flash's light: a small prefab the pop's Lights module lends to its one particle ──
string lightPath = Dir + "/Party confetti flash.prefab";
var lightGo = new GameObject("Party confetti flash");
try
{
    var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.range = 3f; l.intensity = 3.5f; l.shadows = LightShadows.None;
    l.color = new Color(1f, .86f, .62f);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(lightGo, lightPath);
}
finally { UnityEngine.Object.DestroyImmediate(lightGo); }
var lightPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(lightPath).GetComponent<Light>();

var root = new GameObject("Party confetti");
try
{
    // ── Confetti (the root: its stop action destroys the whole burst) ──
    var ps = root.AddComponent<ParticleSystem>(); common(ps);
    var main = ps.main;
    main.duration = 2.1f; main.stopAction = ParticleSystemStopAction.Destroy;
    main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 4.6f);
    main.startSize = 8f / TexelsPerMetre;
    main.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI);
    main.gravityModifier = .38f;
    main.maxParticles = 200;
    main.startColor = new ParticleSystem.MinMaxGradient(palette(new Color32[] {
        new Color32(222, 58, 58, 255), new Color32(238, 138, 40, 255), new Color32(240, 214, 66, 255), new Color32(74, 190, 92, 255),
        new Color32(58, 172, 222, 255), new Color32(92, 104, 226, 255), new Color32(222, 92, 184, 255), new Color32(214, 168, 58, 255) }))
        { mode = ParticleSystemGradientMode.RandomColor };
    var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, 140) });
    var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 70; sh.radius = .1f;
    sh.rotation = new Vector3(-90, 0, 0); sh.randomDirectionAmount = .35f;
    var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 50; lv.drag = 5.2f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
    var no = ps.noise; no.enabled = true; no.strength = .55f; no.frequency = 1.6f; no.scrollSpeed = .6f; no.octaveCount = 1; no.quality = ParticleSystemNoiseQuality.Medium;
    no.positionAmount = 1; no.rotationAmount = 0; no.sizeAmount = 0;
    var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
    var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(fadeAfter(.72f));
    var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 4; ts.numTilesY = 4;
    ts.animation = ParticleSystemAnimationType.SingleRow; ts.rowMode = ParticleSystemAnimationRowMode.Random;
    ts.timeMode = ParticleSystemAnimationTimeMode.Lifetime; ts.cycleCount = 7;
    ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
    ts.startFrame = new ParticleSystem.MinMaxCurve(0, 3);
    var co = ps.collision; co.enabled = true; co.type = ParticleSystemCollisionType.World; co.mode = ParticleSystemCollisionMode.Collision3D;
    co.quality = ParticleSystemCollisionQuality.Medium; co.dampen = .9f; co.bounce = .05f; co.lifetimeLoss = 0; co.radiusScale = .3f;
    co.enableDynamicColliders = false; co.collidesWith = ~0;
    var r = root.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = confettiMat; r.sortingFudge = 0;

    // ── Sparkles: gold and white twinkles among the flecks ──
    var sg = new GameObject("Sparkles"); sg.transform.SetParent(root.transform, false);
    var sp = sg.AddComponent<ParticleSystem>(); common(sp);
    var smain = sp.main;
    smain.duration = 1.0f; smain.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .8f);
    smain.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.6f); smain.startSize = 9f / TexelsPerMetre; smain.gravityModifier = .04f;
    smain.maxParticles = 60;
    smain.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .8f, .34f), new Color(1f, .97f, .88f));
    var sem = sp.emission; sem.rateOverTime = 26; sem.SetBursts(new[] { new ParticleSystem.Burst(0, 12) });
    var ssh = sp.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = .45f; ssh.radiusThickness = 1;
    var slv = sp.limitVelocityOverLifetime; slv.enabled = true; slv.limit = 50; slv.drag = 3.5f;
    var sts = sp.textureSheetAnimation; sts.enabled = true; sts.mode = ParticleSystemAnimationMode.Grid; sts.numTilesX = 4; sts.numTilesY = 1;
    sts.animation = ParticleSystemAnimationType.WholeSheet; sts.cycleCount = 1;
    sts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
    var scol = sp.colorOverLifetime; scol.enabled = true; scol.color = new ParticleSystem.MinMaxGradient(fadeAfter(.8f));
    sg.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkleMat;
    sg.GetComponent<ParticleSystemRenderer>().sortingFudge = -10;

    // ── Pop: one starburst for four frames and the flash's light ──
    var pg = new GameObject("Pop"); pg.transform.SetParent(root.transform, false);
    var pp = pg.AddComponent<ParticleSystem>(); common(pp);
    var pmain = pp.main;
    pmain.duration = .3f; pmain.startLifetime = .13f; pmain.startSpeed = 0; pmain.startSize = 40f / TexelsPerMetre;
    pmain.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI); pmain.maxParticles = 1;
    pmain.startColor = new Color(1f, .93f, .78f);
    var pem = pp.emission; pem.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
    var psh = pp.shape; psh.enabled = false;
    var psz = pp.sizeOverLifetime; psz.enabled = true; psz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .7f), new Keyframe(1, 1.35f)));
    var pcol = pp.colorOverLifetime; pcol.enabled = true; pcol.color = new ParticleSystem.MinMaxGradient(fadeAfter(.3f));
    var pl = pp.lights; pl.enabled = true; pl.light = lightPrefab; pl.ratio = 1; pl.maxLights = 1; pl.useParticleColor = false;
    pl.alphaAffectsIntensity = true; pl.sizeAffectsRange = false; pl.intensityMultiplier = 1; pl.rangeMultiplier = 1;
    pg.GetComponent<ParticleSystemRenderer>().sharedMaterial = popMat;
    pg.GetComponent<ParticleSystemRenderer>().sortingFudge = -20;

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    sb.Append("saved " + PrefabPath);
}
finally { UnityEngine.Object.DestroyImmediate(root); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();
