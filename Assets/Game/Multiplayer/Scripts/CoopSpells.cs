using System.Collections.Generic;
using UnityEngine;

// Co-op: a player's spell seen by the others. The caster's own machine casts it for real (mana, damage, practice); on
// every other machine it is cast again by the caster's stand-in with no power, so it only shows: the bolt flies, the
// ground erupts, the lightning jumps, the Dungeon Master comes for his flick or holds his hand over them. Its blows land
// from the caster's machine (CoopWorld.Intercept sends them to the host). On the host the copy still moves what the spell
// moves (a flicked enemy flies, a raised skeleton gets up for the caster), since the host runs the enemies.
public static class CoopSpells
{
    static Dictionary<string, SpellDefinition> spells;

    // The caster's machine: what was cast and where. a: where it leaves from (the hand), b: its direction or the spot on
    // the ground, eye: the caster's eye, target: the enemy it is aimed at (a flick, a homing bolt).
    public static void Cast(Player caster, SpellDefinition spell, Vector3 a, Vector3 b, Vector3 eye = default, Character target = null)
    {
        if (!Coop.Running || CoopWorld.Floor == null || caster == null || spell == null || caster.kind != PlayerKind.Table) return;
        int id = CoopWorld.TryEnemyId(target, out var enemy) ? enemy + 1 : 0;
        if (CoopPlayer.CoopTiming) Debug.Log($"[Coop] I cast {spell.name} (enemy {id - 1})");
        CoopWorld.Send(new CoopEvent { kind = CoopEventKind.PlayerSpell, item = spell.name, position = a, direction = b, extra = eye, id = id, target = Coop.NoSlot });
    }

    static SpellDefinition Find(string name)
    {
        if (spells == null || !spells.ContainsKey(name))
        {
            spells ??= new Dictionary<string, SpellDefinition>();
            if (InventoryManager.HasInstance)
                foreach (var item in InventoryManager.Instance.itemCatalog)
                {
                    if (item == null) continue;
                    if (item.spell != null) spells[item.spell.name] = item.spell;
                    if (item.scrollSpell != null) spells[item.scrollSpell.name] = item.scrollSpell;
                }
            foreach (var s in Resources.FindObjectsOfTypeAll<SpellDefinition>()) if (s != null) spells.TryAdd(s.name, s);
        }
        return spells.TryGetValue(name, out var found) ? found : null;
    }

    // Everyone else: the caster's stand-in casts it again, for the look.
    public static void Play(CoopEvent e)
    {
        var caster = CoopWorld.CharacterOf(e.slot);
        var spell = Find(e.item.ToString());
        if (caster == null || spell == null) return;
        var target = e.id > 0 ? CoopWorld.Enemy(e.id - 1) : null;
        if (CoopPlayer.CoopTiming) Debug.Log($"[Coop] player {e.slot} cast {spell.name} (enemy {e.id - 1})");
        if (spell.effect != SpellEffect.ShadowVeil) ShadowVeil.Break(caster);
        if (spell.releaseSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.releaseSound, e.position, spell.releaseVolume);
        if (spell.spell == LeftHandSpell.Heal)
        {
            if (spell.impact != null) Object.Instantiate(spell.impact, caster.transform.position, caster.transform.rotation);
            if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.impactSound, caster.transform.position, spell.impactVolume);
            return;
        }
        switch (spell.effect)
        {
            case SpellEffect.ShadowVeil: ShadowVeil.Begin(caster, spell); return;
            case SpellEffect.ChainLightning: ChainLightning.Cast(caster, spell, 0f, e.position, e.extra, e.direction, null, null); return;
            case SpellEffect.SweepingArc: SweepingArc.Cast(caster, spell, 0f, e.position, e.direction, null, null); return;
            case SpellEffect.RaiseTheFallen:
                // The host raises it (its enemies are the real ones) and sends it to the others as a summon on the
                // caster's side; a guest's own copy of the bones would only be taken away again.
                if (!Coop.IsHost) { TakeBones(e.position); return; }
                var heap = BoneHeap.Nearest(e.position, spell.raise.reach + 2f);
                if (heap != null) RaiseTheFallen.Cast(caster, spell, heap, null);
                return;
            case SpellEffect.Flick: if (target != null) Flick.Echo(caster, spell, target); return;
            case SpellEffect.HelpingHand: HelpingHand.Echo(caster, spell, e.direction); return;
        }
        if (spell.groundArea) { GroundAreaSpell.Cast(caster, spell, 0f, e.direction, null); return; }
        if (spell.projectile == null || e.direction.sqrMagnitude < .0001f) return;
        var projectile = Object.Instantiate(spell.projectile, e.position, Quaternion.LookRotation(e.direction.normalized));
        if (spell.homing && target != null) { projectile.HomingTarget = target; projectile.HomingTurn = spell.homingTurn; }
        projectile.Launch(caster, spell, 0f, null);
    }

    // The Raise the Fallen spell (its summon settings, for a raised skeleton that comes from the host).
    public static SpellDefinition RaiseSpell()
    {
        Find("");
        foreach (var s in spells.Values) if (s != null && s.effect == SpellEffect.RaiseTheFallen) return s;
        return null;
    }

    // A guest: the bones nearest a raise are taken up (the host raises them; its skeleton comes as a spawn).
    public static void TakeBones(Vector3 near)
    {
        var heap = BoneHeap.Nearest(near, 6f);
        if (heap == null) return;
        heap.Claim();
        foreach (var p in heap.pieces) if (p.transform != null) Object.Destroy(p.transform.gameObject);
    }
}
