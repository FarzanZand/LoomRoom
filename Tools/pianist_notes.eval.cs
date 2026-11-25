// The Pianist's crescendo burst (Characters/Classes/Pianist/Effects), from the sheets Tools/pianist_notes.py draws there:
//   Crescendo burst.prefab   the 8th hit: four larger notes and four small ones that come out of the hit one after
//                            another and stop 21 to 30 cm from it, each in its own place, and a short warm flash of
//                            light on the hit. Gone in under a second.
// Close to the blow (the user, 2026-11-25: "make it more... Intimate, it bursts so far away from the enemy you barely see
// that it spawned on point of impact"): every note appears 7 cm from the hit point, at the edge of the number as it
// grows in, and is stopped by drag within 30 cm, so the whole burst is 60 cm across, the width of CRESCENDO!
// itself. Until then the notes started 30 cm out to each side and flew up to 90 cm, and the ring opened 30 cm under the
// hit: a burst 1.8 m across round the enemy, not on the blow. Each note has its own direction and distance (thrown at
// random into one fan so close to the hit, they landed on each other in one clump on the number) and its own pair of
// colours to pick from, so every crescendo shows at least four colours.
// No ring any more. A whole ring round the hit ran through the words; dropped 30 cm it stood apart from the blow; as
// two arcs beside the number, among notes this close, it read as blue drips under them. The flash is kept.
// Notes show on the crescendo only (the user, 2026-11-25: "notes only on the crescendo hit"). Until then every hit threw
// two to four small notes (Note burst.prefab: one or two 12 texel notes to each side from 12 cm out, speed 1.4-2.6,
// life .42-.66 s, arcs at -8 and 188 degrees, 34 wide); that prefab is no longer made, and one left from before is removed.
// The burst turns to face the view as it appears (FaceViewOnSpawn: it only turns, the burst stays on the hit point), so
// the notes fly out in the picture's plane, level and down to the left and right: never at the eye (one filled a third
// of the view), not up over the enemy's name bar. CRESCENDO! and the number are centred on the hit, the words just
// above it and the number just under, drawn over the notes; the notes start at the number's edge and end beside and
// under it, below the words. (When the skill popup is up and the hit is high in the view, the label is moved down
// under the popup, and the level notes then lie behind the words' ends.) Round 1 threw twice as
// many, half again as large, in every direction: the crescendo hid what was being fought.
// Dungeon scale (1 unit = 1 m); every particle is drawn at its sheet's cell size, so its pixels are the dungeon's 128
// texels a metre and never scaled over its life. Unlit, with a dark outline, in seven colours picked for themselves. They
// are drawn after the screen filter (the shader LoomRoom/After Filter Particle; the user: "Draw the notes after the
// filter so they keep colour"), so the Pianist's noir look leaves them coloured. Without that shader in the project the
// builder falls back to the Spells Pack's unlit particle material, and noir would grey them.
// It plays on spawn, simulates in world space and destroys itself when done.
// Sets it on Characters/Classes/Pianist/Crescendo.asset (crescendoBurst) when that asset is there.
// Safe to run again: it rebuilds the prefab, materials and light from these numbers.
const string Dir = "Assets/Game/Characters/Classes/Pianist/Effects";
const string FromMat = "Assets/Game/Combat/Spells/_Pack/Materials/Glow_LWRP.mat";
const float TexelsPerMetre = 128f;
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
if (!UnityEditor.AssetDatabase.IsValidFolder(Dir)) return "no " + Dir + ": run python Tools/pianist_notes.py \"" + Dir + "\" first";
var afterFilter = Shader.Find("LoomRoom/After Filter Particle");
sb.AppendLine(afterFilter != null ? "drawn after the screen filter" : "no After Filter Particle shader yet: drawn with the world, under the filter");

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
// A copy of the Spells Pack's glow material (URP particles, unlit, see-through, not additive: the dark outline has to
// show and the reds have to stay deep), soft particles off; with the after-filter shader when there is one.
System.Func<string, Texture2D, float, Material> material = (name, tex, bright) =>
{
    string path = Dir + "/" + name + ".mat";
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path) == null) UnityEditor.AssetDatabase.CopyAsset(FromMat, path);
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    if (afterFilter != null) m.shader = afterFilter;
    m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex);
    m.SetColor("_BaseColor", new Color(bright, bright, bright, 1));
    m.SetFloat("_SoftParticlesEnabled", 0); m.DisableKeyword("_SOFTPARTICLES_ON");
    m.SetFloat("_Blend", 0);
    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
    UnityEditor.EditorUtility.SetDirty(m); return m;
};
// The notes' colours: light and clear, so they stand off a dark wall. Two reds, a deep and a lighter one, and no pink
// (the user, 2026-11-25: "don't use pink, use red instead for the note colors"). Each note picks one of its own two.
Color32 red = new Color32(228, 48, 44, 255), orange = new Color32(255, 170, 60, 255), yellow = new Color32(255, 232, 100, 255), green = new Color32(124, 232, 124, 255),
        blue = new Color32(104, 212, 250, 255), violet = new Color32(178, 148, 255, 255), lightRed = new Color32(246, 96, 72, 255);
System.Func<Color32[], Gradient> palette = cs =>
{
    var g = new Gradient { mode = GradientMode.Fixed };
    var ck = new GradientColorKey[cs.Length];
    for (int i = 0; i < cs.Length; i++) ck[i] = new GradientColorKey(cs[i], (i + 1f) / cs.Length);
    g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
    return g;
};
// Whole until 'from' of its life, then two dimmer steps: fading in hard steps, no smooth ramp.
System.Func<float, Gradient> steps = from =>
{
    var g = new Gradient { mode = GradientMode.Fixed };
    g.SetKeys(new[] { new GradientColorKey(Color.white, 1) },
              new[] { new GradientAlphaKey(1, from), new GradientAlphaKey(.62f, from + (1 - from) * .5f), new GradientAlphaKey(.3f, 1) });
    return g;
};
System.Action<ParticleSystem> common = ps =>
{
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
// One note, thrown in the burst's own flat (which faces the view) 'at' seconds after the blow: 'towards' is its
// direction in degrees (0 right, 180 left, minus is down), give or take 4; 'reach' how far from the hit it stops, give
// or take a tenth (it starts Start from the hit and drag stops it: speed = the rest of the way x Drag). Then it drifts
// up a little. It keeps one shape of its sheet's four and stands nearly upright (a note on its side reads as a blot).
const float Start = .07f, Drag = 10f;
System.Action<ParticleSystem, Material, float, float, Color32, Color32, float, float, float, float> note = (ps, mat, cell, at, one, other, reach, towards, shortest, longest) =>
{
    var main = ps.main;
    main.startLifetime = new ParticleSystem.MinMaxCurve(shortest, longest);
    main.startSpeed = new ParticleSystem.MinMaxCurve((reach - Start) * Drag * .9f, (reach - Start) * Drag * 1.1f);
    main.startSize = cell / TexelsPerMetre;
    main.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
    main.gravityModifier = -.03f;
    main.maxParticles = 2;
    main.startColor = new ParticleSystem.MinMaxGradient(palette(new[] { one, other })) { mode = ParticleSystemGradientMode.RandomColor };
    var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(at, 1) });
    var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Start; sh.radiusThickness = 0;
    sh.arc = 8; sh.arcMode = ParticleSystemShapeMultiModeValue.Random; sh.rotation = new Vector3(0, 0, towards - 4);
    sh.randomDirectionAmount = 0;
    var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 50; lv.drag = Drag; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
    var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(steps(.6f));
    var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 4; ts.numTilesY = 1;
    ts.animation = ParticleSystemAnimationType.WholeSheet; ts.timeMode = ParticleSystemAnimationTimeMode.Lifetime; ts.cycleCount = 1;
    ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f, .999f);      // a random shape per note, kept for its life
    // A note is never drawn larger than this share of the view's height, however near the eye it is.
    var r = ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.maxParticleSize = .09f;
};
System.Func<GameObject, string, ParticleSystem> child = (parent, name) =>
{
    var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
    var ps = go.AddComponent<ParticleSystem>(); common(ps); return ps;
};

var notesMat = material("Notes", texture("Notes"), 1f);
var bigMat = material("Big notes", texture("Big notes"), 1f);

// ── The every-hit burst and the ring are gone: remove what a run before left of them ──
foreach (var gone in new[] { "Note burst.prefab", "Ring.mat", "Ring.png" })
    if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(Dir + "/" + gone) != null)
        sb.AppendLine(UnityEditor.AssetDatabase.DeleteAsset(Dir + "/" + gone) ? "removed " + gone + " (nothing uses it)" : "could not remove " + gone);

// ── The crescendo's flash: a small light that one unseen particle carries for a quarter of a second ──
string lightPath = Dir + "/Crescendo flash.prefab";
var lightGo = new GameObject("Crescendo flash");
try
{
    var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.range = 3f; l.intensity = 3f; l.shadows = LightShadows.None;
    l.color = new Color(1f, .8f, .68f);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(lightGo, lightPath);
}
finally { UnityEngine.Object.DestroyImmediate(lightGo); }
var lightPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(lightPath).GetComponent<Light>();

// ── Crescendo burst: two larger and two small notes to each side, out of the hit one after another; the flash ──
string crescendoPath = Dir + "/Crescendo burst.prefab";
var root = new GameObject("Crescendo burst");
try
{
    root.AddComponent<FaceViewOnSpawn>();
    var ps = root.AddComponent<ParticleSystem>(); common(ps);
    var main = ps.main; main.duration = 1.1f; main.stopAction = ParticleSystemStopAction.Destroy;
    // Each side: a small note level with the hit and far out, a larger one a little down and near (beside the number), a
    // small one further down and far, a larger one steeply down (under the number's corner). No two land on each other,
    // none on the number, none as high as the words. The last one is out a seventh of a second after the blow.
    note(ps, bigMat, 16f, 0f, red, lightRed, .21f, -24f, .55f, .85f);
    System.Action<string, Material, float, float, Color32, Color32, float, float> add = (name, mat, cell, at, one, other, reach, towards) =>
    {
        var c = child(root, name); var cm = c.main; cm.duration = 1f;
        note(c, mat, cell, at, one, other, reach, towards, cell > 12f ? .55f : .5f, cell > 12f ? .85f : .8f);
    };
    add("Right level", notesMat, 12f, .04f, orange, yellow, .29f, -9f);
    add("Right low", notesMat, 12f, .12f, blue, violet, .30f, -42f);
    add("Right under", bigMat, 16f, .08f, green, yellow, .24f, -60f);
    add("Left near", bigMat, 16f, .02f, violet, blue, .21f, 204f);
    add("Left level", notesMat, 12f, .05f, yellow, green, .29f, 189f);
    add("Left low", notesMat, 12f, .14f, lightRed, orange, .30f, 222f);
    add("Left under", bigMat, 16f, .10f, blue, green, .24f, 240f);

    // The flash: one particle that is not drawn, on the hit, carrying the light as it dies away.
    var rp = child(root, "Flash");
    var rmain = rp.main;
    rmain.duration = .4f; rmain.startLifetime = .26f; rmain.startSpeed = 0; rmain.startSize = .1f; rmain.maxParticles = 1;
    var rem = rp.emission; rem.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
    var rsh = rp.shape; rsh.enabled = false;
    var rl = rp.lights; rl.enabled = true; rl.light = lightPrefab; rl.ratio = 1; rl.maxLights = 1; rl.useParticleColor = false;
    rl.alphaAffectsIntensity = false; rl.sizeAffectsRange = false; rl.rangeMultiplier = 1;
    rl.intensity = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.5f, .5f), new Keyframe(1, 0)));
    rp.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.None;

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, crescendoPath);
    sb.AppendLine("saved " + crescendoPath);
}
finally { UnityEngine.Object.DestroyImmediate(root); }
UnityEditor.AssetDatabase.SaveAssets();

// ── On the passive ──
var passive = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Characters/Classes/Pianist/Crescendo.asset");
if (passive == null) sb.AppendLine("no Crescendo.asset yet: the bursts are not set on it");
else
{
    var so = new UnityEditor.SerializedObject(passive);
    var b = so.FindProperty("crescendoBurst");
    if (b == null) sb.AppendLine("Crescendo.asset has no crescendoBurst field");
    else
    {
        b.objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(crescendoPath);
        so.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(passive); UnityEditor.AssetDatabase.SaveAssets();
        sb.AppendLine("set on Crescendo.asset");
    }
}
return sb.ToString();
