using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// How a summoned character behaves: how long it stays, how many one summoner keeps, how it follows and how it ends.
// Kept on whatever summons it (a spell's settings, later an item or a room event).
[Serializable]
public class SummonSettings
{
    [FoldoutGroup("Summon", Expanded = true), Min(0f), Tooltip("Seconds it stays. 0: until it is killed.")]
    public float lifetime = 30f;
    [FoldoutGroup("Summon"), Min(1), Tooltip("How many of this kind one summoner keeps at once; a new one sends the oldest away.")]
    public int maxAtOnce = 2;
    [FoldoutGroup("Summon"), Tooltip("Collapse: it dies where it stands (a skeleton falls into bones). Vanish: it is gone in a puff (End Effect).")]
    public SummonEnd end = SummonEnd.Collapse;
    [FoldoutGroup("Summon"), Min(.5f), Tooltip("With nothing to fight it walks back to within this of its summoner.")]
    public float followDistance = 3f;
    [FoldoutGroup("Summon"), Min(2f), Tooltip("Further than this from its summoner it leaves a fight and comes back.")]
    public float leash = 18f;
    [FoldoutGroup("Summon"), Tooltip("Its kills count as the summoner's (experience, kill count).")]
    public bool creditSummoner = true;
    [FoldoutGroup("Summon"), Tooltip("It drops nothing when killed.")]
    public bool noLoot = true;
    [FoldoutGroup("Summon"), AssetsOnly, Tooltip("Clings to it while summoned (on the player's side: so it is told apart from the enemies).")]
    public SpellVisual aura;
    [FoldoutGroup("Summon"), AssetsOnly, Tooltip("Where it vanishes (End: Vanish), or falls (Collapse).")]
    public SpellVisual endEffect;
}

// Serialized by integer: append only.
public enum SummonEnd { Collapse = 0, Vanish = 1 }

// A character fighting for someone else: summoned by a spell (Raise the Fallen), or by anything that calls Spawn or Bind.
// It takes its summoner's side (Character.FactionOverride), so on the player's side it hunts enemies and enemies fight it
// back (Perception), and on an enemy's side it is just another enemy. With nothing to fight it keeps near its summoner;
// too far away it breaks off and comes back. Its kills count as its summoner's (RunManager). When its time is up, its
// summoner dies or a newer one of its kind takes its place, it ends (collapses or vanishes). Added by Spawn or Bind.
public class Summon : MonoBehaviour
{
    static readonly List<Summon> all = new();
    public static IReadOnlyList<Summon> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    public Character Summoner { get; private set; }
    public Character Body { get; private set; }
    public string Kind { get; private set; }
    public SummonSettings Settings { get; private set; }

    EnemyBrain brain;
    SpellVisual aura;
    float until, born, side = 1f;
    bool ending, returning;

    // A new summon: put down on the floor's navigation near the point (under the summoner's floor), scaled to the floor,
    // already fighting for the summoner. kind: what summoned it (Max At Once counts per kind and summoner).
    public static Summon Spawn(GameObject prefab, Vector3 at, Vector3 facing, Character summoner, SummonSettings settings, string kind, Transform parent = null)
    {
        if (prefab == null || summoner == null || settings == null) return null;
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
        facing.y = 0f;
        if (parent == null) parent = FloorOf(summoner);
        var go = Instantiate(prefab, at, Quaternion.LookRotation(facing.sqrMagnitude > .01f ? facing.normalized : summoner.transform.forward), parent);
        go.name = prefab.name + " (summoned)";
        var body = go.GetComponent<Character>();
        if (body == null) { Destroy(go); return null; }
        var generator = parent != null ? parent.GetComponentInParent<DungeonGenerator>() : null;
        if (generator != null && generator.LevelData != null && generator.LevelData.balance != null) generator.LevelData.balance.Apply(body, generator.FloorNumber, false, generator.LevelData.ExtraFloors(generator.FloorNumber));
        return Bind(body, summoner, settings, kind);
    }

    // An existing character made a summon (a body put down by the spell itself, like the skeleton Raise the Fallen builds
    // out of bones).
    public static Summon Bind(Character body, Character summoner, SummonSettings settings, string kind)
    {
        if (body == null || summoner == null || settings == null) return null;
        var summon = body.GetComponent<Summon>();
        if (summon == null) summon = body.gameObject.AddComponent<Summon>();
        summon.Body = body; summon.Summoner = summoner; summon.Settings = settings; summon.Kind = kind;
        summon.born = Time.time;
        summon.side = UnityEngine.Random.value < .5f ? -1f : 1f;
        summon.until = settings.lifetime > 0f ? Time.time + settings.lifetime : float.MaxValue;
        if (body.Faction != summoner.Faction) body.FactionOverride = summoner.Faction;
        if (settings.noLoot && body.TryGetComponent<DungeonLootDrop>(out var drop)) Destroy(drop);
        summon.brain = body.GetComponent<EnemyBrain>();
        if (!all.Contains(summon)) all.Add(summon);
        // Max At Once: the oldest of this kind that this summoner keeps goes.
        var mine = all.FindAll(s => s != null && s != summon && !s.ending && s.Summoner == summoner && s.Kind == kind);
        mine.Sort((a, b) => a.born.CompareTo(b.born));
        for (int i = 0; i < mine.Count - (settings.maxAtOnce - 1); i++) mine[i].Dismiss();
        return summon;
    }

    // Shows its aura (once it is seen: a body raised from bones is hidden until they close).
    public void ShowAura()
    {
        if (aura != null || Settings == null || Settings.aura == null) return;
        aura = Instantiate(Settings.aura, transform.position + Vector3.up * .05f, Quaternion.identity, transform);
    }

    // Who answers for a character: its summoner (and theirs, up the chain), or itself.
    public static Character Commander(Character c)
    {
        for (int guard = 0; c != null && guard < 4; guard++)
        {
            var s = c.GetComponent<Summon>();
            if (s == null || s.Summoner == null || !s.Settings.creditSummoner) return c;
            c = s.Summoner;
        }
        return c;
    }

    // The nearest living summon hostile to this character within radius (enemies fight the player's summons back).
    public static Character NearestHostile(Character self, Vector3 from, float radius)
    {
        Character best = null;
        float bestDistance = radius * radius;
        foreach (var s in all)
        {
            if (s == null || s.ending || s.Body == null || !s.Body.IsAlive || s.Body == self) continue;
            if (self != null && !FactionRules.IsHostile(self.Faction, s.Body.Faction)) continue;
            float d = (s.transform.position - from).sqrMagnitude;
            if (d < bestDistance) { best = s.Body; bestDistance = d; }
        }
        return best;
    }

    void OnDestroy()
    {
        all.Remove(this);
        if (brain != null && brain.MovementOverride == Steer) brain.MovementOverride = null;
    }

    void Update()
    {
        if (ending || Body == null) return;
        if (!Body.IsAlive) { all.Remove(this); if (aura != null) aura.Stop(); enabled = false; return; }
        if (Time.time >= until || Summoner == null || !Summoner.IsAlive) { Dismiss(); return; }
        if (brain != null && brain.MovementOverride == null) brain.MovementOverride = Steer;
    }

    // Keeps it with its summoner: back to them when it has nothing to fight or has strayed past the leash; standing by
    // them when close and idle, out of the way of their view. False lets the brain fight.
    bool Steer()
    {
        if (Summoner == null || brain == null || brain.Motor == null) return false;
        float distance = Perception.HorizontalDist(transform.position, Summoner.transform.position);
        bool fighting = brain.Perception != null && brain.Perception.Target != null && brain.Perception.Target.IsAlive;
        if (fighting && distance <= Settings.leash) return false;
        // Its place: beside and a little behind, each on its own side.
        var forward = Facing(Summoner);
        var right = Vector3.Cross(Vector3.up, forward);
        var place = Summoner.transform.position + right * 1.2f * side - forward;
        // Close in front of the summoner it is in the way (a player's view): it goes to its place too.
        var toMe = transform.position - Summoner.transform.position; toMe.y = 0f;
        bool inTheWay = distance < 2.5f && toMe.sqrMagnitude > .01f && Vector3.Dot(forward, toMe.normalized) > .5f;
        if (distance > Settings.followDistance || inTheWay) returning = true;
        if (returning && Perception.HorizontalDist(transform.position, place) < .5f) returning = false;
        if (returning)
        {
            brain.Motor.MoveTo(place);
            return true;
        }
        brain.Motor.Stop();
        return true;
    }

    // Which way a character faces: a player where they look, anyone else where its body points.
    static Vector3 Facing(Character c)
    {
        var f = c is Player p && p.Look != null && p.Look.YawTransform != null ? p.Look.YawTransform.forward : c.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > .001f ? f.normalized : Vector3.forward;
    }

    // Its time is up: it collapses where it stands or vanishes.
    public void Dismiss()
    {
        if (ending) return;
        ending = true;
        all.Remove(this);
        if (aura != null) aura.Stop();
        if (Settings != null && Settings.endEffect != null) Instantiate(Settings.endEffect, transform.position + Vector3.up * .8f, Quaternion.identity);
        if (Body == null) { Destroy(this); return; }
        if (Settings != null && Settings.end == SummonEnd.Vanish || !Body.IsAlive) { Destroy(Body.gameObject); return; }
        var end = DamageInfo.Simple(Body.Stats.CurrentHealth * 10f + 1000f);
        end.Magic = true; end.Quiet = true; end.SourceName = "time";
        Body.Stats.TakeDamage(end);
    }

    // Under the summoner's floor (so it goes with the floor), or loose.
    static Transform FloorOf(Character summoner)
    {
        var generator = summoner.GetComponentInParent<DungeonGenerator>();
        if (generator != null) return generator.transform;
        // The player is not under the floor: the floor being played (a pocket would be the portal's, not this one).
        var loader = TableManager.Loader;
        return loader != null && loader.Dungeon != null ? loader.Dungeon.transform : null;
    }
}
