using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class SweepingArcSettings
{
    [FoldoutGroup("Sweeping arc", Expanded = true), Range(20f, 220f), Tooltip("Degrees the beam sweeps across, centred on the aim.")]
    public float arc = 120f;
    [FoldoutGroup("Sweeping arc"), Min(.1f), Tooltip("Seconds from one end of the sweep to the other.")]
    public float sweepSeconds = .55f;
    [FoldoutGroup("Sweeping arc"), Min(1f), Tooltip("How far the beam reaches (walls cut it short).")]
    public float length = 10f;
    [FoldoutGroup("Sweeping arc"), Tooltip("Height of an enemy's beam above its feet: head height, so a crouching player ducks under it.")]
    public float enemyHeight = 1.45f;
    [FoldoutGroup("Sweeping arc"), Min(0f), Tooltip("Seconds an enemy's beam shows where it will start before it moves: the warning.")]
    public float warnSeconds = .45f;
    [FoldoutGroup("Sweeping arc"), Min(.1f), Tooltip("Seconds an enemy's beam takes across: slower than the player's, so it can be seen coming and ducked.")]
    public float enemySweepSeconds = .85f;
    [FoldoutGroup("Sweeping arc"), Min(1f), Tooltip("An enemy's beam is drawn this much wider: it is seen nearly edge on, at eye height.")]
    public float enemyWidthScale = 1.6f;
    [FoldoutGroup("Look"), Min(.01f)] public float coreWidth = .09f;
    [FoldoutGroup("Look"), Min(.01f)] public float glowWidth = .5f;
    [FoldoutGroup("Look"), AssetsOnly] public Material coreMaterial;
    [FoldoutGroup("Look"), AssetsOnly] public Material glowMaterial;
    [FoldoutGroup("Look"), ColorUsage(false, true)] public Color color = new(1.4f, .35f, 1.5f);
    [FoldoutGroup("Look"), Min(0f)] public float lightIntensity = 5f;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("Burns at the beam's end, where it meets a wall or reaches its length.")]
    public SpellVisual tipEffect;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("Spawned on every character the beam cuts.")]
    public SpellVisual hitEffect;
    [FoldoutGroup("Look"), Tooltip("Loops while the beam sweeps.")]
    public AudioClip sweepSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float sweepVolume = .8f;
}

// Sweeping Arc: a beam from the hand that swings across the caster's front from one side to the other, cutting every
// hostile it crosses once. Walls stop it. The player's sweeps at hand height and catches everything it passes; an
// enemy's runs at head height (Enemy Height) after a short warning line, so the player can duck under it by crouching.
public class SweepingArc : MonoBehaviour
{
    Character owner;
    SpellDefinition spell;
    SweepingArcSettings settings;
    AdventurerProgress progress;
    float power, elapsed, startYaw;
    Vector3 origin;
    bool enemyBeam, hurt, finished;
    LineRenderer core, glow;
    Light tipLight;
    SpellVisual tip;
    AudioSource loop;
    readonly HashSet<Character> cut = new();
    List<Character> near;

    // forward: the middle of the sweep (flat). only: an enemy's target (its beam runs at head height and can be ducked).
    public static void Cast(Character owner, SpellDefinition spell, float power, Vector3 hand, Vector3 forward, Character only, AdventurerProgress progress)
    {
        var go = new GameObject(spell.name + " (beam)");
        var arc = go.AddComponent<SweepingArc>();
        arc.owner = owner; arc.spell = spell; arc.settings = spell.sweep; arc.power = power; arc.progress = progress;
        arc.enemyBeam = only != null;
        forward.y = 0f;
        if (forward.sqrMagnitude < .001f) forward = owner != null ? owner.transform.forward : Vector3.forward;
        // Swept from the caster's left to its right.
        arc.startYaw = Quaternion.LookRotation(forward.normalized).eulerAngles.y - spell.sweep.arc * .5f;
        arc.origin = hand;
        if (arc.enemyBeam && owner != null) arc.origin = new Vector3(hand.x, owner.transform.position.y + spell.sweep.enemyHeight, hand.z);
        arc.Begin();
    }

    void Begin()
    {
        glow = Line("Glow", settings.glowMaterial);
        core = Line("Core", settings.coreMaterial);
        if (settings.tipEffect != null) tip = Instantiate(settings.tipEffect, origin, Quaternion.identity, transform);
        if (settings.lightIntensity > 0f)
        {
            var go = new GameObject("Tip light");
            go.transform.SetParent(transform, false);
            tipLight = go.AddComponent<Light>();
            tipLight.type = LightType.Point; tipLight.range = 5f; tipLight.shadows = LightShadows.None;
            float m = Mathf.Max(settings.color.r, Mathf.Max(settings.color.g, settings.color.b));
            tipLight.color = m > 0f ? settings.color / m : Color.white;
        }
        if (settings.sweepSound != null && AudioManager.HasInstance)
        {
            loop = gameObject.AddComponent<AudioSource>();
            loop.spatialBlend = 1f; loop.minDistance = 3f; loop.maxDistance = 30f;
            AudioManager.Instance.PlaySFXLoop(loop, settings.sweepSound, settings.sweepVolume);
        }
        near = SpellEffects.HostilesNear(owner, origin, settings.length + 1f);
        transform.position = origin;
        Step(0f, 0f);
    }

    LineRenderer Line(string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2;
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        return line;
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        // A dead caster drops the beam.
        if (owner != null && !owner.IsAlive) { Finish(); return; }
        float before = elapsed;
        elapsed += Time.deltaTime;
        float warn = enemyBeam ? settings.warnSeconds : 0f;
        float seconds = SweepSeconds;
        float from = Mathf.Clamp01((before - warn) / seconds), to = Mathf.Clamp01((elapsed - warn) / seconds);
        Step(from, to);
        if (elapsed >= warn + seconds + .08f) Finish();
    }

    float SweepSeconds => enemyBeam ? settings.enemySweepSeconds : settings.sweepSeconds;

    Vector3 Direction(float t) => Quaternion.Euler(0f, startYaw + settings.arc * Mathf.SmoothStep(0f, 1f, t), 0f) * Vector3.forward;

    // Cuts everything crossed between t0 and t1 of the sweep and draws the beam at t1.
    void Step(float t0, float t1)
    {
        bool warning = enemyBeam && elapsed < settings.warnSeconds;
        var dir = Direction(t1);
        float reach = Reach(dir);
        if (!warning && t1 > t0)
        {
            float yaw0 = Yaw(Direction(t0)), yaw1 = Yaw(dir);
            foreach (var c in near)
            {
                if (c == null || !c.IsAlive || cut.Contains(c)) continue;
                var chest = SpellEffects.Chest(c);
                var flat = chest - origin; flat.y = 0f;
                float d = flat.magnitude;
                if (d > settings.length + .5f) continue;
                // Inside the slice swept this frame (a little wider for the body's own width).
                float yaw = Yaw(flat), pad = Mathf.Rad2Deg * Mathf.Atan2(.35f, Mathf.Max(.5f, d));
                if (!Between(yaw, yaw0 - pad, yaw1 + pad)) continue;
                if (d > Reach(flat.normalized) + .4f) continue;   // behind a wall
                if (enemyBeam && Ducks(c)) continue;
                cut.Add(c);
                var at = new Vector3(chest.x, enemyBeam ? origin.y : chest.y, chest.z);
                if (settings.hitEffect != null) Instantiate(settings.hitEffect, at, Quaternion.identity);
                if (SpellEffects.Strike(owner, c, power, at, origin, spell.displayName)) hurt = true;
            }
        }
        var end = origin + dir * reach;
        float alpha = warning ? .25f + .15f * Mathf.Sin(elapsed * 40f) : 1f;
        var col = settings.color;
        // Thin where it leaves the hand, full width a metre and a half out: a wide glow right at the eye fills the view.
        float swell = Mathf.Clamp01(1.5f / Mathf.Max(.1f, reach));
        float wide = enemyBeam ? settings.enemyWidthScale : 1f;
        Set(glow, origin, end, (warning ? settings.glowWidth * .35f : settings.glowWidth) * wide, new Color(col.r, col.g, col.b, .6f * alpha), swell);
        Set(core, origin, end, (warning ? settings.coreWidth * .5f : settings.coreWidth) * wide, new Color(Mathf.Max(col.r, 1f), Mathf.Max(col.g, 1f), Mathf.Max(col.b, 1f), alpha), swell);
        if (tip != null) { tip.transform.position = end; tip.gameObject.SetActive(!warning); }
        if (tipLight != null) { tipLight.transform.position = end - dir * .3f; tipLight.intensity = warning ? 0f : settings.lightIntensity; }
    }

    // A crouching player is under an enemy's head-height beam.
    bool Ducks(Character c) => c is Player p && p.Motor != null && p.Motor.IsCrouching;

    // How far the beam runs this way before a wall (characters and invisible rims let it through).
    float Reach(Vector3 dir)
    {
        float reach = settings.length;
        var hits = Physics.RaycastAll(origin, dir, settings.length, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.distance >= reach) continue;
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.collider.GetComponentInParent<Character>() != null || InvisibleWall.Is(hit.collider) || DeathPhysics.IsCorpse(hit.collider)) continue;
            if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue;   // loose bones, debris
            reach = hit.distance;
        }
        return reach;
    }

    static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    static bool Between(float yaw, float a, float b)
    {
        float span = Mathf.DeltaAngle(a, b), off = Mathf.DeltaAngle(a, yaw);
        if (span < 0f) { span = -span; off = -off; }
        return off >= 0f && off <= span;
    }

    static void Set(LineRenderer line, Vector3 a, Vector3 b, float width, Color color, float swell)
    {
        if (line == null) return;
        line.SetPosition(0, a); line.SetPosition(1, b);
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, .15f), new Keyframe(swell, 1f), new Keyframe(1f, 1f));
        line.startColor = line.endColor = color;
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        if (hurt) progress?.Practise(spell.Skill, SkillAction.Cast);
        if (tip != null) tip.Stop();
        Destroy(gameObject);
    }
}
