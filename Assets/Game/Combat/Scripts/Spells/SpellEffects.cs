using UnityEngine;

// The spells that do more than fly, land or heal (SpellDefinition.effect): one way in for the player and one for enemy
// casters, so both cast the same assets. Each effect lives in its own script beside this one.
public static class SpellEffects
{
    // Why the player can't cast it right now (null: they can). Checked before the wind-up and again at the release.
    public static string Refusal(SpellDefinition spell, Player player)
    {
        if (spell.effect == SpellEffect.RaiseTheFallen && BoneHeap.Nearest(player.transform.position, spell.raise.reach) == null)
            return "No bones close enough";
        return null;
    }

    // The player: aimed along the view (where the crosshair points), leaving from the hand.
    public static void CastByPlayer(Player player, SpellDefinition spell, float power, Transform view, Vector3 hand, AdventurerProgress progress)
    {
        switch (spell.effect)
        {
            case SpellEffect.ShadowVeil: ShadowVeil.Begin(player, spell); break;
            case SpellEffect.ChainLightning: ChainLightning.Cast(player, spell, power, hand, view.position, view.forward, null, progress); break;
            case SpellEffect.SweepingArc: SweepingArc.Cast(player, spell, power, hand, view.forward, null, progress); break;
            case SpellEffect.RaiseTheFallen:
                var heap = BoneHeap.Nearest(player.transform.position, spell.raise.reach);
                if (heap != null) RaiseTheFallen.Cast(player, spell, heap, progress);
                break;
        }
    }

    // Whether an enemy can use it now: a raise needs bones near the caster, a veil isn't an enemy's.
    public static bool CanCast(SpellDefinition spell, Character caster)
    {
        if (spell == null) return false;
        return spell.effect switch
        {
            SpellEffect.RaiseTheFallen => BoneHeap.Nearest(caster.transform.position, spell.raise.reach) != null,
            SpellEffect.ShadowVeil => false,
            _ => true,
        };
    }

    // An enemy caster: at its target.
    public static void CastByEnemy(Character caster, SpellDefinition spell, float power, Vector3 hand, Character target)
    {
        switch (spell.effect)
        {
            case SpellEffect.ChainLightning:
                var aim = target.transform.position + Vector3.up * 1.1f;
                ChainLightning.Cast(caster, spell, power, hand, hand, (aim - hand).normalized, target, null);
                break;
            case SpellEffect.SweepingArc:
                var toward = target.transform.position - caster.transform.position; toward.y = 0f;
                SweepingArc.Cast(caster, spell, power, hand, toward.sqrMagnitude > .01f ? toward.normalized : caster.transform.forward, target, null);
                break;
            case SpellEffect.RaiseTheFallen:
                var heap = BoneHeap.Nearest(caster.transform.position, spell.raise.reach);
                if (heap != null) RaiseTheFallen.Cast(caster, spell, heap, null);
                break;
        }
    }

    // Magic damage straight onto one character, as a spell's blast would land it.
    public static bool Strike(Character owner, Character target, float power, Vector3 at, Vector3 from, string sourceName = null)
    {
        if (target == null || !target.IsAlive || target == owner) return false;
        if (owner != null && !FactionRules.IsHostile(owner.Faction, target.Faction)) { ProvokableNpc.TryProvoke(target, owner); return false; }
        var dir = at - from; dir.y = 0f;
        target.Stats.TakeDamage(new DamageInfo { Amount = power, Source = owner, SourceName = sourceName, Magic = true, HitPoint = at, Direction = dir.sqrMagnitude > .0001f ? dir.normalized : Vector3.forward });
        return true;
    }

    // A character's middle, where bolts and beams meet it.
    public static Vector3 Chest(Character c)
    {
        var col = c.GetComponent<Collider>();
        return col != null ? col.bounds.center + Vector3.up * col.bounds.extents.y * .25f : c.transform.position + Vector3.up * 1.1f;
    }

    // Living characters hostile to the owner within radius (the owner's own faction and the dead left out).
    public static System.Collections.Generic.List<Character> HostilesNear(Character owner, Vector3 centre, float radius)
    {
        var list = new System.Collections.Generic.List<Character>();
        foreach (var col in Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var c = col.GetComponentInParent<Character>();
            if (c == null || c == owner || !c.IsAlive || list.Contains(c)) continue;
            if (owner != null && !FactionRules.IsHostile(owner.Faction, c.Faction)) continue;
            list.Add(c);
        }
        return list;
    }
}
