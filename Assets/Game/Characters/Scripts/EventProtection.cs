using System.Collections.Generic;
using UnityEngine;

// Characters the Dungeon Master keeps out of reach for now: an event's fighter waiting for the player to walk up and
// talk (Little Undead John on the duel bridge, the Bridge Troll, the Arena's Black Knight). The user, 2026-11-13: John
// was killed from the far end of the bridge with spells before he had said a word. While held, a hit does nothing
// (CharacterStats.TakeDamage: no health lost, so no knockback, flinch, blood or number), and nothing stuns, slows, burns
// or moves it (EnemyBrain.Stun, Chill, Burning, Flick and the Dice Portal). A blow from the player (or their summon)
// shows a small puff where it landed, and the Dungeon Master stops it with one of his lines
// (DungeonMasterRemarks.protectedHit, not again within its gap). The event lets go as its talk begins (Release); from
// then on its own rules apply (the duel's house rule three). One shared hold for every such event: an event script
// calls Hold when it finds its fighter and Release when the talk starts.
public static class EventProtection
{
    static readonly Dictionary<Character, Object> held = new();
    static float lastPuff;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { held.Clear(); lastPuff = 0f; }

    // Kept out of reach by this event (by: the event, for the record) until Release.
    public static void Hold(Character c, Object by) { if (c != null) held[c] = by; }
    public static void Release(Character c) { if (c != null) held.Remove(c); }
    public static bool Held(Character c) => held.Count > 0 && c != null && held.ContainsKey(c);

    // A blow or spell on a held character: shown as stopped (a small puff at, and his line when the player or their
    // summon sent it). True when held, and then the caller does nothing more.
    public static bool Stops(Character c, Character by, Vector3 at)
    {
        if (!Held(c)) return false;
        bool player = by is Player || by != null && by.TryGetComponent<Summon>(out _);
        if (!player) return true;
        DungeonMasterRemarks.ProtectedHit(Puff(c, at));
        return true;
    }

    // A small puff on the body where the blow was stopped (the spawn puff of the level defaults, at .8: smaller it was
    // a few specks nobody saw), with
    // the blocked hit's sound. Not more than a few a second (a blast and its burn tick land together).
    static Vector3 Puff(Character c, Vector3 at)
    {
        if (at == Vector3.zero || (at - c.transform.position).sqrMagnitude > 9f) at = c.transform.position + Vector3.up * 1.2f;
        // Just in front of the body as the player sees it, so it isn't hidden inside it.
        var cam = Camera.main != null ? Camera.main.transform : null;
        if (cam != null) at += (cam.position - at).normalized * .25f;
        if (Time.time - lastPuff < .12f) return at;
        lastPuff = Time.time;
        PuffAt(at);
        return at;
    }

    // The puff and its sound at a spot (co-op: a teammate's stopped blow, shown here as well).
    public static void PuffAt(Vector3 at)
    {
        var defaults = TableLevelDefaults.Shared;
        var prefab = defaults != null ? defaults.ambushSpawnPuff : null;
        if (prefab != null)
        {
            var puff = Object.Instantiate(prefab, at, Quaternion.identity);
            puff.transform.localScale = Vector3.one * .8f;
            foreach (var ps in puff.GetComponentsInChildren<ParticleSystem>()) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            Object.Destroy(puff, 4f);
        }
        if (CombatManager.HasInstance && CombatManager.Instance.blockAudio != null && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFXData(CombatManager.Instance.blockAudio, at);
    }
}
