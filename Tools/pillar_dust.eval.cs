// Makes Levels/Shared/Props/Effects/Pillar dust.prefab: the dust of a boss stage's cover pillar as it breaks
// (BossCoverPillar.dust), and points the Boss stage's two Cover pillars at it. Safe to run again (it rewrites the prefab).
// The breakables' Dust puff is pale and made for a barrel in a lit room; on the dark stage, up a three metre pier, it was
// the brightest thing in view. This one is kept low, in the stone's own shadow tones: the table reveal's soft dust
// (Reveal/Entrance dust, see-through: solid dark squares in front of lit things looked like holes in the picture) and
// the house's small solid squares for the chips:
//   Foot   rolls out along the floor from the pillar's foot in three waves, as the lumps land (it slows and shrinks away)
//   Grit   small chips thrown off the breaking stone, falling
//   Haze   a few big dim puffs rising slowly off the stump, the last to go
// Sizes are for the stage's pier (1.6 x 3 x 1.9 m).
string path = "Assets/Game/Levels/Shared/Props/Effects/Pillar dust.prefab";
var square = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Game/Combat/Effects/Impact.mat");
var soft = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Game/Levels/Shared/Reveal/Entrance dust.mat");
if (square == null || soft == null) return "no Impact or Entrance dust material";

UnityEngine.ParticleSystem System(UnityEngine.GameObject go, float duration, UnityEngine.Material material)
{
    var ps = go.AddComponent<UnityEngine.ParticleSystem>();
    ps.Stop(true, UnityEngine.ParticleSystemStopBehavior.StopEmittingAndClear);
    var main = ps.main;
    main.duration = duration; main.loop = false; main.playOnAwake = true;
    main.simulationSpace = UnityEngine.ParticleSystemSimulationSpace.World;
    main.scalingMode = UnityEngine.ParticleSystemScalingMode.Hierarchy;
    var r = go.GetComponent<UnityEngine.ParticleSystemRenderer>();
    r.sharedMaterial = material; r.renderMode = UnityEngine.ParticleSystemRenderMode.Billboard;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    r.sortMode = UnityEngine.ParticleSystemSortMode.None;
    return ps;
}
UnityEngine.ParticleSystem.MinMaxGradient Tones(UnityEngine.Color a, UnityEngine.Color b) => new UnityEngine.ParticleSystem.MinMaxGradient(a, b);
// Full by 'full' of its life, fading out from 'from' on.
UnityEngine.ParticleSystem.MinMaxGradient Fade(float full, float from)
{
    var g = new UnityEngine.Gradient();
    g.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.white, 0f), new UnityEngine.GradientColorKey(UnityEngine.Color.white, 1f) },
        new[] { new UnityEngine.GradientAlphaKey(0f, 0f), new UnityEngine.GradientAlphaKey(1f, full), new UnityEngine.GradientAlphaKey(1f, from), new UnityEngine.GradientAlphaKey(0f, 1f) });
    return new UnityEngine.ParticleSystem.MinMaxGradient(g);
}
UnityEngine.AnimationCurve Curve(params float[] timeValue)
{
    var keys = new UnityEngine.Keyframe[timeValue.Length / 2];
    for (int i = 0; i < keys.Length; i++) keys[i] = new UnityEngine.Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
    var curve = new UnityEngine.AnimationCurve(keys);
    for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
    return curve;
}

var root = new UnityEngine.GameObject("Pillar dust");
try
{
    // ── Foot: out along the floor ──
    {
        var ps = System(root, 1.2f, soft);
        var main = ps.main;
        main.startLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(.8f, 1.5f);
        main.startSpeed = new UnityEngine.ParticleSystem.MinMaxCurve(1.4f, 3.6f);
        main.startSize = new UnityEngine.ParticleSystem.MinMaxCurve(.22f, .5f);
        main.startColor = Tones(new UnityEngine.Color(.42f, .35f, .27f, .7f), new UnityEngine.Color(.6f, .5f, .38f, .9f));
        main.gravityModifier = 0f; main.maxParticles = 120;
        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new UnityEngine.ParticleSystem.Burst(0f, 26), new UnityEngine.ParticleSystem.Burst(.28f, 20), new UnityEngine.ParticleSystem.Burst(.55f, 16),
        });
        // A ring lying on the floor round the pillar's foot: the dust leaves it outward.
        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = UnityEngine.ParticleSystemShapeType.Circle; shape.radius = .95f; shape.radiusThickness = .5f; shape.arc = 360f;
        shape.position = new UnityEngine.Vector3(0f, .14f, 0f); shape.rotation = new UnityEngine.Vector3(-90f, 0f, 0f);
        shape.randomDirectionAmount = .12f;
        var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = .25f; limit.dampen = .09f;
        var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = UnityEngine.ParticleSystemSimulationSpace.World;
        drift.x = new UnityEngine.ParticleSystem.MinMaxCurve(0f, 0f); drift.z = new UnityEngine.ParticleSystem.MinMaxCurve(0f, 0f);
        drift.y = new UnityEngine.ParticleSystem.MinMaxCurve(.05f, .45f);
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new UnityEngine.ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .3f, 1f, 1f, 1.35f));
        var fade = ps.colorOverLifetime; fade.enabled = true; fade.color = Fade(.12f, .45f);
    }
    // ── Grit: chips off the stone ──
    {
        var go = new UnityEngine.GameObject("Grit"); go.transform.SetParent(root.transform, false);
        var ps = System(go, 1f, square);
        var main = ps.main;
        main.startLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(.5f, 1f);
        main.startSpeed = new UnityEngine.ParticleSystem.MinMaxCurve(.8f, 3.8f);
        main.startSize = new UnityEngine.ParticleSystem.MinMaxCurve(.03f, .075f);
        main.startColor = Tones(new UnityEngine.Color(.16f, .13f, .1f, 1f), new UnityEngine.Color(.5f, .4f, .27f, 1f));
        main.gravityModifier = 1.4f; main.maxParticles = 140;
        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new UnityEngine.ParticleSystem.Burst(0f, 70), new UnityEngine.ParticleSystem.Burst(.22f, 30) });
        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = UnityEngine.ParticleSystemShapeType.Box; shape.scale = new UnityEngine.Vector3(1.4f, 2.3f, 1.6f);
        shape.position = new UnityEngine.Vector3(0f, 1.7f, 0f); shape.randomDirectionAmount = 1f;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new UnityEngine.ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .7f, .9f, 1f, 0f));
    }
    // ── Haze: off the stump ──
    {
        var go = new UnityEngine.GameObject("Haze"); go.transform.SetParent(root.transform, false);
        var ps = System(go, 1f, soft);
        var main = ps.main;
        main.startDelay = .2f;
        main.startLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(1.3f, 2.3f);
        main.startSpeed = 0f;
        main.startSize = new UnityEngine.ParticleSystem.MinMaxCurve(.4f, .75f);
        main.startColor = Tones(new UnityEngine.Color(.36f, .3f, .24f, .4f), new UnityEngine.Color(.5f, .42f, .33f, .6f));
        main.gravityModifier = 0f; main.maxParticles = 30;
        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new UnityEngine.ParticleSystem.Burst(0f, 8), new UnityEngine.ParticleSystem.Burst(.35f, 6) });
        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = UnityEngine.ParticleSystemShapeType.Box; shape.scale = new UnityEngine.Vector3(1.5f, .5f, 1.7f);
        shape.position = new UnityEngine.Vector3(0f, 1f, 0f);
        var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = UnityEngine.ParticleSystemSimulationSpace.World;
        drift.x = new UnityEngine.ParticleSystem.MinMaxCurve(-.15f, .15f); drift.z = new UnityEngine.ParticleSystem.MinMaxCurve(-.15f, .15f);
        drift.y = new UnityEngine.ParticleSystem.MinMaxCurve(.25f, .6f);
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new UnityEngine.ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .4f, 1f, 1f, 1.4f));
        var fade = ps.colorOverLifetime; fade.enabled = true; fade.color = Fade(.25f, .5f);
    }
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
}
finally { UnityEngine.Object.DestroyImmediate(root); }

// The two Cover pillars take it.
var dust = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
string stagePath = "Assets/Game/Levels/Shared/Rooms/Scripted/Boss stage.prefab";
var stage = UnityEditor.PrefabUtility.LoadPrefabContents(stagePath);
int set = 0;
try
{
    foreach (var pillar in stage.GetComponentsInChildren<BossCoverPillar>(true)) { pillar.dust = dust; UnityEditor.EditorUtility.SetDirty(pillar); set++; }
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(stage, stagePath);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(stage); }
return "Pillar dust made; set on " + set + " cover pillars";
