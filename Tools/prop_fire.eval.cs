// Fire for the dungeon's wall torches and braziers (the night review, 2026-11-15: a torch's flame was one soft orange
// blob and a brazier had a light but no flame at all). Run as an eval (safe to run again):
//   unity command eval_file --file Tools/prop_fire.eval.cs
// Makes Levels/Shared/Props/Effects/Prop fire.prefab (a bowl's fire) and Torch fire.prefab (narrower and taller, for a
// torch's basket): the Fireball's own flame flipbook in pixels (the pack's Spell 1, as the burn on a body uses it),
// small flames overlapping into one fire, with embers rising off it. Its colour is the
// particles' start colour, so an eerie room (DungeonRoomMood.Recolour) and the boss stage's fires (BossStageLights)
// tint it as they tinted the old flames. Then it puts one on the Wall torch (its old glow blob switched off) and on the
// Brazier (with a breathing light), and switches off the flames the Boss stage added to its braziers itself.
const string Dir = "Assets/Game/Levels/Shared/Props/Effects";
var fireMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Combat/Spells/_Pack/Materials/Spell 1_LWRP.mat");
var emberMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Combat/Spells/Fireball/Burn/Ember.mat");
if (fireMat == null || emberMat == null) return "no fire or ember material";
var log = new System.Text.StringBuilder();

ParticleSystem System(GameObject parent, string name, Material mat, int tiles, bool overLife)
{
    var go = new GameObject(name);
    go.transform.SetParent(parent.transform, false);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    var r = go.GetComponent<ParticleSystemRenderer>();
    r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    r.maxParticleSize = .25f;   // point blank, a flame never fills the view
    var sheet = ps.textureSheetAnimation;
    sheet.enabled = true; sheet.numTilesX = tiles; sheet.numTilesY = 1; sheet.animation = ParticleSystemAnimationType.WholeSheet;
    if (overLife) { sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, .999f)); sheet.startFrame = 0; }
    else { sheet.startFrame = new ParticleSystem.MinMaxCurve(0, (tiles - .01f) / tiles); sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0); }
    sheet.cycleCount = 1;
    var main = ps.main;
    // Local and scaled with the hierarchy: a brazier's fire is this prefab made larger.
    main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
    main.loop = true; main.playOnAwake = true; main.duration = 1; main.prewarm = true;
    return ps;
}

// ── The fire: flame width and height, how fast it rises, flames a second, tint, how wide its foot is ──
GameObject Fire(string name, Vector2 wide, Vector2 tall, Vector2 rise, float rate, Color tint, float foot, bool core)
{
var fire = new GameObject(name);
var flames = System(fire, "Flames", fireMat, 8, true);
{
    var main = flames.main; main.maxParticles = 32;
    main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .7f); main.startSpeed = 0;
    // The tint (the flame's material draws about three times as bright, so it is kept low).
    main.startColor = tint;
    main.startSize3D = true;
    main.startSizeX = new ParticleSystem.MinMaxCurve(wide.x, wide.y); main.startSizeY = new ParticleSystem.MinMaxCurve(tall.x, tall.y); main.startSizeZ = 1;
    main.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
    var em = flames.emission; em.rateOverTime = rate;
    var shape = flames.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(foot, .02f, foot);
    var vel = flames.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
    vel.x = new ParticleSystem.MinMaxCurve(-.05f, .05f); vel.y = new ParticleSystem.MinMaxCurve(rise.x, rise.y); vel.z = new ParticleSystem.MinMaxCurve(-.05f, .05f);
    var size = flames.sizeOverLifetime; size.enabled = true;
    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(.25f, 1f), new Keyframe(1, .5f)));
    // Bright at its foot, redder and darker as it thins out: shades of the start colour, so a tint carries through.
    var g = new Gradient();
    g.SetKeys(new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0f), new GradientColorKey(new Color(.93f, .5f, .2f), .45f), new GradientColorKey(new Color(.5f, .16f, .04f), 1f) },
              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(.8f, .55f), new GradientAlphaKey(0f, 1f) });
    var colour = flames.colorOverLifetime; colour.enabled = true; colour.color = g;
    flames.GetComponent<ParticleSystemRenderer>().flip = new Vector3(.5f, 0, 0);   // half mirrored, so the wisps don't all lean one way
}
// A hot heart low in the fire (a torch's): small pale flames at its foot, so the flame has a bright middle against a
// lit wall as well as in the dark. A shade of the same tint, so a room that tints the fire tints its heart.
if (core)
{
    var heart = System(flames.gameObject, "Heart", fireMat, 8, true);
    var main = heart.main; main.maxParticles = 16;
    main.startLifetime = new ParticleSystem.MinMaxCurve(.28f, .42f); main.startSpeed = 0;
    main.startColor = Color.Lerp(tint, Color.white, .55f);
    main.startSize3D = true;
    main.startSizeX = new ParticleSystem.MinMaxCurve(wide.x * .55f, wide.y * .6f); main.startSizeY = new ParticleSystem.MinMaxCurve(tall.x * .5f, tall.y * .55f); main.startSizeZ = 1;
    main.startRotation = new ParticleSystem.MinMaxCurve(-.2f, .2f);
    var em = heart.emission; em.rateOverTime = rate * .6f;
    var shape = heart.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(foot * .5f, .02f, foot * .5f);
    var vel = heart.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
    vel.x = new ParticleSystem.MinMaxCurve(-.02f, .02f); vel.y = new ParticleSystem.MinMaxCurve(rise.x * .45f, rise.y * .45f); vel.z = new ParticleSystem.MinMaxCurve(-.02f, .02f);
    var g = new Gradient();
    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .85f, .6f), 1f) },
              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(.9f, .6f), new GradientAlphaKey(0f, 1f) });
    var colour = heart.colorOverLifetime; colour.enabled = true; colour.color = g;
    heart.GetComponent<ParticleSystemRenderer>().flip = new Vector3(.5f, 0, 0);
}
// Embers rising and drifting off it: under the flames, so the two are one effect to the table's reveal.
var embers = System(flames.gameObject, "Embers", emberMat, 2, false);
{
    var main = embers.main; main.maxParticles = 12;
    main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.5f); main.startSpeed = 0;
    main.startSize = new ParticleSystem.MinMaxCurve(.025f, .04f); main.startColor = Color.white;
    main.simulationSpace = ParticleSystemSimulationSpace.World;   // they drift on their own once off the fire
    var em = embers.emission; em.rateOverTime = 4;
    var shape = embers.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(.12f, .1f, .12f);
    var vel = embers.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
    vel.x = new ParticleSystem.MinMaxCurve(-.1f, .1f); vel.y = new ParticleSystem.MinMaxCurve(.45f, .95f); vel.z = new ParticleSystem.MinMaxCurve(-.1f, .1f);
    var noise = embers.noise; noise.enabled = true; noise.strength = .25f; noise.frequency = 1.1f;
    var g = new Gradient { mode = GradientMode.Fixed };
    g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
              new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .7f), new GradientAlphaKey(.5f, .86f), new GradientAlphaKey(0, 1) });
    var colour = embers.colorOverLifetime; colour.enabled = true; colour.color = g;
}
var made = UnityEditor.PrefabUtility.SaveAsPrefabAsset(fire, Dir + "/" + name + ".prefab");
UnityEngine.Object.DestroyImmediate(fire);
log.AppendLine("made " + Dir + "/" + name + ".prefab");
return made;
}
var bowlFire = Fire("Prop fire", new Vector2(.2f, .28f), new Vector2(.3f, .42f), new Vector2(.22f, .45f), 22, new Color(1f, .5f, .22f, 1f), .1f, false);
// A torch's: against a lit wall a low wide fire was a faint blob, so it is narrow, tall and a little brighter.
var torchFire = Fire("Torch fire", new Vector2(.16f, .22f), new Vector2(.38f, .54f), new Vector2(.34f, .6f), 32, new Color(1f, .62f, .3f, 1f), .05f, true);

// One fire under a prop, at a place and size (an older one made by this tool is replaced).
void Put(GameObject prop, GameObject saved, Vector3 at, float scale)
{
    var old = prop.transform.Find("Prop fire");
    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var inst = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(saved, prop.transform);
    inst.name = "Prop fire";
    inst.transform.localPosition = at; inst.transform.localScale = Vector3.one * scale;
}

// ── The wall torch: the fire at its head, its old glow blob switched off ──
{
    const string path = "Assets/Game/Levels/Shared/Props/Furnishing/Wall torch.prefab";
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    var blob = root.transform.Find("Flame");
    // In the basket's mouth: a flame is drawn round its middle, so lower it burned inside the basket, hidden by its bars.
    Vector3 at = blob != null ? blob.localPosition + Vector3.up * .07f : new Vector3(-.02f, 2.4f, .33f);
    if (blob != null) blob.gameObject.SetActive(false);
    Put(root, torchFire, at, 1.25f);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path); UnityEditor.PrefabUtility.UnloadPrefabContents(root);
    log.AppendLine("wall torch: fire at " + at);
}
// ── The brazier: a bigger fire in its bowl, and its light breathes like a torch's ──
{
    const string path = "Assets/Game/Levels/Shared/Props/Furnishing/Brazier.prefab";
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    Put(root, bowlFire, new Vector3(0f, .84f, 0f), 1.7f);
    var light = root.GetComponentInChildren<Light>(true);
    if (light != null && light.GetComponent<TorchFlicker>() == null) light.gameObject.AddComponent<TorchFlicker>();
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path); UnityEditor.PrefabUtility.UnloadPrefabContents(root);
    log.AppendLine("brazier: fire in the bowl");
}
// ── The boss stage: its braziers had flames of their own added; the brazier's own fire takes their place ──
{
    const string path = "Assets/Game/Levels/Shared/Rooms/Scripted/Boss stage.prefab";
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    int off = 0;
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
        if (t.name == "Flame" && t.parent != null && t.parent.name.StartsWith("Brazier") && t.gameObject.activeSelf) { t.gameObject.SetActive(false); off++; }
    if (off > 0) UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
    log.AppendLine("boss stage: " + off + " old brazier flames switched off");
}
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
