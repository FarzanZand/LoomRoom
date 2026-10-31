using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class ChainLightningSettings
{
    [FoldoutGroup("Chain lightning", Expanded = true), Min(1f), Tooltip("Furthest the first target may be.")]
    public float range = 16f;
    [FoldoutGroup("Chain lightning"), Range(0f, 45f), Tooltip("Degrees off the crosshair a target may stand and still be struck first.")]
    public float aimCone = 14f;
    [FoldoutGroup("Chain lightning"), Min(0), Tooltip("Further targets it jumps to after the first.")]
    public int jumps = 3;
    [FoldoutGroup("Chain lightning"), Min(.5f), Tooltip("Furthest one jump reaches.")]
    public float jumpRange = 7f;
    [FoldoutGroup("Chain lightning"), Range(.1f, 1f), Tooltip("Each jump hits this share of the one before.")]
    public float falloff = .75f;
    [FoldoutGroup("Chain lightning"), Min(0f), Tooltip("Enemies struck stand stunned this long.")]
    public float stunSeconds = .35f;
    [FoldoutGroup("Chain lightning"), Min(0f), Tooltip("Seconds between one jump and the next.")]
    public float jumpDelay = .09f;
    [FoldoutGroup("Look"), Min(.05f), Tooltip("Seconds a bolt stays on screen.")]
    public float boltSeconds = .32f;
    [FoldoutGroup("Look"), Min(.01f)] public float coreWidth = .07f;
    [FoldoutGroup("Look"), Min(.01f)] public float glowWidth = .45f;
    [FoldoutGroup("Look"), Min(0f), Tooltip("How far the bolt wanders off the straight line, in metres at its middle.")]
    public float kink = .45f;
    [FoldoutGroup("Look"), Min(.01f), Tooltip("Seconds between new shapes of the crackling bolt.")]
    public float flicker = .04f;
    [FoldoutGroup("Look"), AssetsOnly] public Material coreMaterial;
    [FoldoutGroup("Look"), AssetsOnly] public Material glowMaterial;
    [FoldoutGroup("Look"), ColorUsage(false, true)] public Color color = new(.55f, .85f, 1.6f);
    [FoldoutGroup("Look"), Min(0f), Tooltip("A light flashes at every target struck.")]
    public float lightIntensity = 6f;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("Spawned on every target struck, and where a bolt meets a wall.")]
    public SpellVisual hitEffect;
    [FoldoutGroup("Look")] public AudioClip jumpSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float jumpVolume = .8f;
}

// Chain Lightning: a bolt from the hand to the enemy under the crosshair (or the nearest close to it), then on to the
// next enemy near the last, and the next, each hit weaker, never the same enemy twice and never through a wall. Each
// enemy struck is stunned for a moment. Cast at the player by an enemy, it strikes only them. The bolts are crackling
// lines, a white core in a blue glow, that jump to a new shape every Flicker and fade out over Bolt Seconds.
public class ChainLightning : MonoBehaviour
{
    class Bolt
    {
        public LineRenderer core, glow;
        public Light light;
        public Vector3 from, to;
        public Character target;
        public float born;
        public int seed;
    }

    readonly List<Bolt> bolts = new();
    ChainLightningSettings settings;
    bool running;

    public static void Cast(Character owner, SpellDefinition spell, float power, Vector3 hand, Vector3 eye, Vector3 forward, Character only, AdventurerProgress progress)
    {
        var go = new GameObject(spell.name + " (bolts)");
        var chain = go.AddComponent<ChainLightning>();
        chain.settings = spell.chain;
        chain.StartCoroutine(chain.Run(owner, spell, power, hand, eye, forward, only, progress));
    }

    IEnumerator Run(Character owner, SpellDefinition spell, float power, Vector3 hand, Vector3 eye, Vector3 forward, Character only, AdventurerProgress progress)
    {
        running = true;
        var s = settings;
        Character first = only;
        Vector3 miss = eye + forward * s.range;
        if (first == null) first = FirstTarget(owner, eye, forward, ref miss);
        else if (!LineOfSight.Clear(hand, SpellEffects.Chest(first), ~0, owner != null ? owner.transform : null, first.transform))
        {
            if (Physics.Linecast(hand, SpellEffects.Chest(first), out var wall, ~0, QueryTriggerInteraction.Ignore)) miss = wall.point;
            first = null;
        }

        if (first == null)
        {
            // Nothing to strike: the bolt cracks into the wall or the air where it was aimed.
            AddBolt(hand, miss, null);
            Hit(miss, null);
            yield return Fade();
            yield break;
        }

        var struck = new List<Character>();
        Vector3 from = hand;
        Character target = first;
        float damage = power;
        bool hurt = false;
        for (int i = 0; target != null && i <= s.jumps; i++)
        {
            var at = SpellEffects.Chest(target);
            AddBolt(from, at, target);
            Hit(at, target);
            struck.Add(target);
            if (SpellEffects.Strike(owner, target, damage, at, from, spell.displayName)) hurt = true;
            if (target.IsAlive && s.stunSeconds > 0f && target.TryGetComponent<EnemyBrain>(out var brain)) brain.Stun(s.stunSeconds);
            if (only != null) break;
            from = at;
            damage *= s.falloff;
            target = NextTarget(owner, from, struck, target);
            if (target != null && s.jumpDelay > 0f) yield return new WaitForSeconds(s.jumpDelay);
        }
        if (hurt) progress?.Practise(spell.Skill, SkillAction.Cast);
        yield return Fade();
    }

    // The enemy under the crosshair (a fat ray along the view), else the one nearest the crosshair within the aim cone.
    // miss: where the bolt goes when there is none (the wall the view meets).
    Character FirstTarget(Character owner, Vector3 eye, Vector3 forward, ref Vector3 miss)
    {
        var hits = Physics.SphereCastAll(eye, .4f, forward, settings.range, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            if (InvisibleWall.Is(hit.collider) || DeathPhysics.IsCorpse(hit.collider)) continue;
            var c = hit.collider.GetComponentInParent<Character>();
            if (c != null)
            {
                if (c.IsAlive && (owner == null || FactionRules.IsHostile(owner.Faction, c.Faction))) return c;
                continue;
            }
            // A wall: what stands behind it can't be struck.
            miss = hit.distance > 0f ? hit.point : eye + forward * .5f;
            break;
        }
        Character best = null;
        float bestAngle = settings.aimCone;
        foreach (var c in SpellEffects.HostilesNear(owner, eye, settings.range))
        {
            var chest = SpellEffects.Chest(c);
            float angle = Vector3.Angle(forward, chest - eye);
            if (angle > bestAngle) continue;
            if (!LineOfSight.Clear(eye, chest, ~0, owner != null ? owner.transform : null, c.transform)) continue;
            best = c; bestAngle = angle;
        }
        return best;
    }

    Character NextTarget(Character owner, Vector3 from, List<Character> struck, Character last)
    {
        Character best = null;
        float bestDistance = float.MaxValue;
        foreach (var c in SpellEffects.HostilesNear(owner, from, settings.jumpRange))
        {
            if (struck.Contains(c)) continue;
            var chest = SpellEffects.Chest(c);
            float d = Vector3.Distance(from, chest);
            if (d >= bestDistance) continue;
            if (!LineOfSight.Clear(from, chest, ~0, last != null ? last.transform : null, c.transform)) continue;
            best = c; bestDistance = d;
        }
        return best;
    }

    void Hit(Vector3 at, Character target)
    {
        if (settings.hitEffect != null) Instantiate(settings.hitEffect, at, Quaternion.identity);
        if (settings.jumpSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(settings.jumpSound, at, settings.jumpVolume);
    }

    void AddBolt(Vector3 from, Vector3 to, Character target)
    {
        var bolt = new Bolt { from = from, to = to, target = target, born = Time.time, seed = UnityEngine.Random.Range(0, 100000) };
        bolt.glow = Line("Glow", settings.glowMaterial);
        bolt.core = Line("Core", settings.coreMaterial);
        if (settings.lightIntensity > 0f)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(transform, false);
            go.transform.position = to;
            bolt.light = go.AddComponent<Light>();
            bolt.light.type = LightType.Point; bolt.light.range = 6f; bolt.light.shadows = LightShadows.None;
            bolt.light.color = Normalised(settings.color);
            bolt.light.intensity = settings.lightIntensity;
        }
        bolts.Add(bolt);
        Draw(bolt);
    }

    LineRenderer Line(string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        return line;
    }

    IEnumerator Fade()
    {
        running = false;
        while (bolts.Count > 0) yield return null;
        Destroy(gameObject);
    }

    void Update()
    {
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            float age = (Time.time - b.born) / Mathf.Max(.05f, settings.boltSeconds);
            if (age >= 1f)
            {
                if (b.core != null) Destroy(b.core.gameObject);
                if (b.glow != null) Destroy(b.glow.gameObject);
                if (b.light != null) Destroy(b.light.gameObject);
                bolts.RemoveAt(i);
                continue;
            }
            // The bolt stays on what it struck as it reels back; a body that burst keeps the last spot.
            if (b.target != null && b.target.gameObject.activeInHierarchy && b.target.IsAlive) b.to = SpellEffects.Chest(b.target);
            Draw(b);
            float fade = 1f - age * age;
            if (b.light != null) { b.light.transform.position = b.to; b.light.intensity = settings.lightIntensity * fade; }
        }
        if (!running && bolts.Count == 0) Destroy(gameObject);
    }

    // A jagged line: kinks every half metre or so, most at the middle, none at the ends; a new shape every Flicker.
    void Draw(Bolt b)
    {
        float age = Mathf.Clamp01((Time.time - b.born) / Mathf.Max(.05f, settings.boltSeconds));
        int step = Mathf.FloorToInt((Time.time - b.born) / Mathf.Max(.01f, settings.flicker));
        var rng = new System.Random(b.seed + step * 7919);
        float length = Vector3.Distance(b.from, b.to);
        int count = Mathf.Clamp(Mathf.CeilToInt(length / .45f) + 1, 3, 48);
        var dir = (b.to - b.from).normalized;
        var side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < .01f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        var up = Vector3.Cross(side, dir).normalized;
        var points = new Vector3[count];
        float kink = Mathf.Min(settings.kink, length * .12f);
        for (int i = 0; i < count; i++)
        {
            float u = i / (count - 1f);
            var p = Vector3.Lerp(b.from, b.to, u);
            if (i > 0 && i < count - 1)
            {
                float k = Mathf.Sin(u * Mathf.PI) * kink;
                p += side * ((float)rng.NextDouble() * 2f - 1f) * k + up * ((float)rng.NextDouble() * 2f - 1f) * k;
            }
            points[i] = p;
        }
        float fade = 1f - age * age;
        // Bright at first, flickering as it dies.
        float flick = age < .35f ? 1f : (rng.NextDouble() < .3 ? .35f : 1f);
        var c = settings.color;
        // Thin where it leaves the hand, full width a metre and a half out: drawn full width right at the eye the glow is a box.
        float swell = Mathf.Clamp01(1.5f / Mathf.Max(.1f, length));
        Set(b.glow, points, settings.glowWidth * (.8f + (float)rng.NextDouble() * .4f), new Color(c.r, c.g, c.b, .55f * fade * flick), swell);
        Set(b.core, points, settings.coreWidth * (.8f + (float)rng.NextDouble() * .5f), new Color(Mathf.Max(c.r, 1f), Mathf.Max(c.g, 1f), Mathf.Max(c.b, 1f), fade * flick), swell);
    }

    static void Set(LineRenderer line, Vector3[] points, float width, Color color, float swell)
    {
        if (line == null) return;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, .15f), new Keyframe(swell, 1f), new Keyframe(1f, 1f));
        line.startColor = line.endColor = color;
    }

    static Color Normalised(Color c) { float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return m > 0f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white; }
}
